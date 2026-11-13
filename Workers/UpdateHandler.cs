using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramVideoBot.Services;
using TelegramVideoBot.Utilities;

namespace TelegramVideoBot.Workers;

public class UpdateHandler(EnvironmentConfig config, S3StorageService s3, ILogger<UpdateHandler> logger) : IUpdateHandler
{
    private readonly Dictionary<long, DownloadManager> downloadManagers = [];
    private readonly string botName = config.TelegramBotName ?? "Frozen's Video Bot";
    private readonly ILogger<UpdateHandler> logger = logger;
    private readonly EnvironmentConfig config = config;
    private readonly S3StorageService s3 = s3;

    public async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken = new())
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQuery(client, callbackQuery, cancellationToken);
            return;
        }

        if (update.Message is not { } message) return;
        if (message.Text is not { } messageText) return;

        var splitMessage = messageText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (splitMessage.Length < 1) return;

        if (splitMessage[0].StartsWith("/download"))
        {
            await HandleDownload(client, message, cancellationToken);
            return;
        }

        if (splitMessage[0].StartsWith('/'))
        {
            await HandleHelp(client, message, cancellationToken);
            return;
        }

        await HandleDownload(client, message, cancellationToken);
    }

    public async Task HandlePollingErrorAsync(ITelegramBotClient client, Exception ex, CancellationToken cancellationToken = new())
    {
        logger.LogError(ex, ex.Message);
        await Task.CompletedTask;
    }

    private async Task HandleDownload(ITelegramBotClient client, Message message, CancellationToken cancellationToken = new())
    {
        var userId = message.SenderUserId();
        if (userId == 0) return;

        if (message.Text is not { } messageText) return;

        var downloadUrls = messageText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (downloadUrls.Length == 0 || (downloadUrls.Length == 1 && downloadUrls[0].StartsWith("/download")))
        {
            await client.ReplyPrivately(message, "No URL included in message.", logger, cancellationToken: cancellationToken);
            return;
        }

        if (downloadUrls[0].StartsWith("/download"))
        {
            downloadUrls = downloadUrls[1..];
        }

        if (!downloadManagers.TryGetValue(userId, out var manager))
        {
            manager = new(client, userId, config.DownloadQueueLimit, config.FileSizeLimit, s3, logger);
            downloadManagers.Add(userId, manager);
        }

        var queueStatuses = new Dictionary<string, Enums.DownloadQueueStatus>();

        foreach (var url in downloadUrls)
        {
            if (!queueStatuses.ContainsKey(url))
            {
                queueStatuses.Add(url, manager.QueueDownload(new DownloadInfo
                {
                    Trigger = message,
                    VideoUrl = url
                }));
            }
        }

        string Section(Enums.DownloadQueueStatus status, string heading)
        {
            var urls = queueStatuses.Where(pair => pair.Value == status).Select(pair => Extensions.MarkdownV2Code(pair.Key)).ToList();
            return urls.Count == 0 ? "" : $"{heading}\n{string.Join('\n', urls)}";
        }

        var queued = Section(Enums.DownloadQueueStatus.Success, "Queueing the following videos:");
        var errors = string.Join("\n\n", new[]
        {
            Section(Enums.DownloadQueueStatus.InvalidUrl, "The following video URLs are invalid:"),
            Section(Enums.DownloadQueueStatus.QueueFull, "The following video URLs weren't queued due to a full queue:"),
            Section(Enums.DownloadQueueStatus.UnknownError, "The following video URLs weren't queued due to an unknown error:")
        }.Where(section => section != ""));

        if (queued != "") await client.ReplyPrivately(message, queued, logger, ParseMode.MarkdownV2, cancellationToken: cancellationToken);
        if (errors != "") await client.ReplyPrivately(message, errors, logger, ParseMode.MarkdownV2, cancellationToken: cancellationToken);
    }

    private async Task HandleHelp(ITelegramBotClient client, Message message, CancellationToken cancellationToken = new())
    {
        if (message.Text is not { } messageText) return;

        var replyBuilder = new StringBuilder($"Hello there, I'm {botName}\\! I download videos from URLs you send me and send them back to you as video files\\.");
        replyBuilder.AppendLine();
        replyBuilder.AppendLine();
        replyBuilder.AppendLine("Please note that the Telegram API limits me to 50 MB attachments per message, so long videos may take longer to process due to compression\\. *Please be patient\\!*");
        replyBuilder.AppendLine();
        replyBuilder.AppendLine("To get started, send a message starting with `/download` followed by a URL to a video, and I'll do my best\\!");
        replyBuilder.AppendLine();
        replyBuilder.AppendLine("\\(I also work without the `/download` command in case you want to use a video app's share feature to send me a video URL directly\\!\\)");

        try
        {
            await client.ReplyPrivately(message, replyBuilder.ToString(), logger, ParseMode.MarkdownV2, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }

        replyBuilder = new StringBuilder($"Also, each user can queue up to {config.DownloadQueueLimit} videos at a time. You can do this by sending multiple messages or alternatively sending multiple video links within the same message separated by line breaks or spaces.");

        try
        {
            await client.ReplyPrivately(message, replyBuilder.ToString(), logger, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }
    }

    private async Task HandleCallbackQuery(ITelegramBotClient client, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Data is not { } data) return;

        var parts = data.Split(':', 2);
        if (parts.Length != 2) return;

        var action = parts[0];
        var pendingId = parts[1];

        if (action is not ("transcode" or "original")) return;

        Task Answer(string? text = null, bool showAlert = false) => client.AnswerCallbackQuery(callbackQuery.Id, text, showAlert, cancellationToken: cancellationToken);
        const string choiceGone = "This choice is no longer available.";

        if (!DownloadManager.PendingTranscodeChoices.TryGetValue(pendingId, out var pending))
        {
            await Answer(choiceGone, true);
            return;
        }

        var userId = pending.Trigger.SenderUserId();
        if (callbackQuery.From.Id != userId)
        {
            await Answer("Only the original requester can do this.");
            return;
        }

        var isTranscode = action == "transcode";
        if (!isTranscode)
        {
            // The link only goes out privately, and bots can't message users who never started a chat with them.
            // Check before taking the choice, so the user can start a chat and press the button again.
            try
            {
                await client.SendChatAction(userId, ChatAction.UploadDocument, cancellationToken: cancellationToken);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
            {
                logger.LogInformation("Can't message user {UserId} privately ({Error})", userId, ex.Message);
                await Answer("I can't message you privately yet. Open a private chat with me, press Start, then press \"Get original\" again.", true);
                return;
            }
        }

        // Taking the choice stops the expiry from deleting the file while it's in use, and makes a double click a no-op.
        if (!DownloadManager.PendingTranscodeChoices.TryRemove(pendingId, out _))
        {
            await Answer(choiceGone, true);
            return;
        }

        await Answer();

        // Swap the buttons for a status.
        if (callbackQuery.Message is { } prompt)
        {
            try
            {
                var status = isTranscode ? "Transcoding, please wait." : "Uploading the original video. I'll send you the link in a private chat.";
                if (prompt.EphemeralMessageId is { } ephemeralId)
                    await client.EditEphemeralMessageText(prompt.Chat.Id, userId, ephemeralId, status, cancellationToken: cancellationToken);
                else
                    await client.EditMessageText(prompt.Chat.Id, prompt.MessageId, status, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update the transcode prompt");
            }
        }

        if (!downloadManagers.TryGetValue(userId, out var manager))
        {
            manager = new(client, userId, config.DownloadQueueLimit, config.FileSizeLimit, s3, logger);
            downloadManagers[userId] = manager;
        }

        manager.HandleTranscodeChoice(pending, isTranscode);
    }

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        if (source is HandleErrorSource.HandleUpdateError) throw exception;

        logger.LogError(exception, exception.Message);

        await Task.CompletedTask;
    }
}
