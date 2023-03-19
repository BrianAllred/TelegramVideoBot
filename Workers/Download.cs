namespace TelegramVideoBot.Workers;

public class DownloadInfo
{
    public long ChatId { get; set; }
    public int ReplyId { get; set; }
    public string? VideoUrl { get; set; }
    public bool BetterQuality { get; set; }
}