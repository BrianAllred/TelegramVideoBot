using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramVideoBot.Utilities;

namespace TelegramVideoBot.Workers;

public class UpdateHandler(EnvironmentConfig config, ILogger<UpdateHandler> logger) : IUpdateHandler
{
    private readonly Dictionary<long, DownloadManager> downloadManagers = new();
    private readonly string botName = config.TelegramBotName ?? "Frozen's Video Bot";
    private readonly ILogger<UpdateHandler> logger = logger;
    private readonly EnvironmentConfig config = config;

    public async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken = new())
    {
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
            await client.SendMessage(message.Chat.Id, "No URL included in message.", replyParameters: message.MessageId, cancellationToken: cancellationToken);
            return;
        }

        if (downloadUrls[0].StartsWith("/download"))
        {
            downloadUrls = downloadUrls[1..];
        }

        if (!downloadManagers.TryGetValue(userId, out var manager))
        {
            manager = new(client, userId, config.DownloadQueueLimit, config.FileSizeLimit, logger);
            downloadManagers.Add(userId, manager);
        }

        var queueStatuses = new Dictionary<string, Enums.DownloadQueueStatus>();

        foreach (var url in downloadUrls)
        {
            if (!queueStatuses.ContainsKey(url))
            {
                queueStatuses.Add(url, manager.QueueDownload(new DownloadInfo
                {
                    ChatId = message.Chat.Id,
                    ReplyId = message.MessageId,
                    VideoUrl = url
                }));
            }
        }

        var replyBuilder = new StringBuilder();
        if (queueStatuses.Values.Any(status => status == Enums.DownloadQueueStatus.Success))
        {
            replyBuilder.AppendLine("Successfully queued the following videos:");
            replyBuilder.AppendJoin('\n', queueStatuses.Where(pair => pair.Value == Enums.DownloadQueueStatus.Success).Select(pair => $"`{pair.Key}`"));
        }

        replyBuilder.AppendLine();

        if (queueStatuses.Values.Any(status => status == Enums.DownloadQueueStatus.InvalidUrl))
        {
            replyBuilder.AppendLine("The following video URLs are invalid:");
            replyBuilder.AppendJoin('\n', queueStatuses.Where(pair => pair.Value == Enums.DownloadQueueStatus.InvalidUrl).Select(pair => $"`{pair.Key}`"));
        }

        replyBuilder.AppendLine();

        if (queueStatuses.Values.Any(status => status == Enums.DownloadQueueStatus.QueueFull))
        {
            replyBuilder.AppendLine("The following video URLs weren't queued due to a full queue:");
            replyBuilder.AppendJoin('\n', queueStatuses.Where(pair => pair.Value == Enums.DownloadQueueStatus.QueueFull).Select(pair => $"`{pair.Key}`"));
        }

        replyBuilder.AppendLine();

        if (queueStatuses.Values.Any(status => status == Enums.DownloadQueueStatus.UnknownError))
        {
            replyBuilder.AppendLine("The following video URLs weren't queued due to an unknown error:");
            replyBuilder.AppendJoin('\n', queueStatuses.Where(pair => pair.Value == Enums.DownloadQueueStatus.UnknownError).Select(pair => $"`{pair.Key}`"));
        }

        await client.SendMessage(message.Chat.Id, replyBuilder.ToString(), replyParameters: message.MessageId, parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
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
            await client.SendMessage(message.Chat.Id, replyBuilder.ToString(), parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }

        replyBuilder = new StringBuilder($"Also, each user can queue up to {config.DownloadQueueLimit} videos at a time. You can do this by sending multiple messages or alternatively sending multiple video links within the same message separated by line breaks or spaces.");

        try
        {
            await client.SendMessage(message.Chat.Id, replyBuilder.ToString(), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }
    }

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        if (source is HandleErrorSource.HandleUpdateError) throw exception;

        logger.LogError(exception, exception.Message);

        await Task.CompletedTask;
    }
}
