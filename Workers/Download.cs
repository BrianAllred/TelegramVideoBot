using Telegram.Bot.Types;

namespace TelegramVideoBot.Workers;

public class DownloadInfo
{
    public required Message Trigger { get; set; }
    public required string VideoUrl { get; set; }
}

public class PendingTranscode
{
    public required string FilePath { get; set; }
    public required Message Trigger { get; set; }
}