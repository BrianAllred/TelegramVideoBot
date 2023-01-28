using System.Runtime.CompilerServices;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;
using TelegramVideoBot.Utilities;

namespace TelegramVideoBot.Workers
{
    public class Telegram : IHostedService
    {
        private readonly Dictionary<long, DownloadManager> downloadManagers = new();
        private readonly TelegramBotClient client;
        private readonly QueuedUpdateReceiver updateReceiver;
        private readonly string botName;

        private bool _started;

        public Telegram(EnvironmentConfig config)
        {
            client = new TelegramBotClient(config.TelegramBotToken ?? "")
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            updateReceiver = new QueuedUpdateReceiver(client);
            botName = config.TelegramBotName ?? "Frozen's Video Bot";
        }

        private async Task HandleUpdateAsync(ITelegramBotClient _, Update update)
        {
            if (update.Message is not { } message) return;
            if (message.Text is not { } messageText) return;

            var splitMessage = messageText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (splitMessage.Length < 1) return;

            if (splitMessage[0].StartsWith('/'))
            {
                await HandleHelp(message);
                return;
            }

            await HandleDownload(message);
        }

        private async Task HandleDownload(Message message)
        {
            long userId = message.SenderUserId();
            if (userId == 0) return;

            if (message.Text is not { } messageText) return;

            var downloadUrls = messageText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (downloadUrls.Length < 1)
            {
                await client.SendTextMessageAsync(message.Chat.Id, "No URL included in message.", replyToMessageId: message.MessageId);
                return;
            }

            if (!downloadManagers.TryGetValue(userId, out var manager))
            {
                manager = new(client, userId);
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

            await client.SendTextMessageAsync(message.Chat.Id, replyBuilder.ToString(), replyToMessageId: message.MessageId, parseMode: ParseMode.MarkdownV2);
        }

        private async Task HandleHelp(Message message)
        {
            if (message.Text is not { } messageText) return;

            var replyBuilder = new StringBuilder($"Hello there, I'm {botName}\\! I download videos from URLs you send me and send them back to you as video files\\.");
            replyBuilder.AppendLine();
            replyBuilder.AppendLine();
            replyBuilder.AppendLine("Please note that the Telegram API limits me to 50 MB attachments per message, so long videos may take longer to process due to compression\\. *Please be patient\\!*");
            replyBuilder.AppendLine();
            replyBuilder.AppendLine("To get started, send a message with a URL to a video, and I'll do my best\\!");

            try
            {
                await client.SendTextMessageAsync(message.Chat.Id, replyBuilder.ToString(), ParseMode.MarkdownV2);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_started) return;

            _started = true;

            updateReceiver.StartReceiving(cancellationToken: cancellationToken);

            await foreach (var update in updateReceiver.YieldUpdatesAsync())
            {
                await HandleUpdateAsync(client, update);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            updateReceiver.StopReceiving();
            await client.CloseAsync(cancellationToken);
        }
    }
}