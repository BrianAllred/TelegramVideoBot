using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramVideoBot.Utilities;

namespace TelegramVideoBot.Workers;

public class UpdateHandler : IUpdateHandler
{
    private readonly Dictionary<long, DownloadManager> downloadManagers = new();
    private readonly string botName;
    private readonly int queueLimit;

    public UpdateHandler(EnvironmentConfig config)
    {
        botName = config.TelegramBotName ?? "Frozen's Video Bot";
        queueLimit = config.DownloadQueueLimit;
    }

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
        Console.WriteLine(ex);
        await Task.CompletedTask;
    }

    private async Task HandleDownload(ITelegramBotClient client, Message message, CancellationToken cancellationToken = new())
    {
        long userId = message.SenderUserId();
        if (userId == 0) return;

        if (message.Text is not { } messageText) return;

        var downloadUrls = messageText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (downloadUrls.Length == 0 || (downloadUrls.Length == 1 && downloadUrls[0] == "/download"))
        {
            await client.SendTextMessageAsync(message.Chat.Id, "No URL included in message.", replyToMessageId: message.MessageId, cancellationToken: cancellationToken);
            return;
        }

        if (downloadUrls[0] == "/download")
        {
            downloadUrls = downloadUrls[1..];
        }

        if (!downloadManagers.TryGetValue(userId, out var manager))
        {
            manager = new(client, userId, queueLimit);
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

        await client.SendTextMessageAsync(message.Chat.Id, replyBuilder.ToString(), replyToMessageId: message.MessageId, parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
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
        replyBuilder.AppendLine("\\(I also work without the `/download` command in case you want to use a video app's share feature\\!\\)");

        try
        {
            await client.SendTextMessageAsync(message.Chat.Id, replyBuilder.ToString(), parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        replyBuilder = new StringBuilder($"Also, each user can queue up to {queueLimit} videos at a time. You can do this by sending multiple messages or alternatively sending multiple video links within the same message separated by line breaks or spaces.");

        try
        {
            await client.SendTextMessageAsync(message.Chat.Id, replyBuilder.ToString(), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}
