namespace TelegramVideoBot.Workers;

public class DownloadInfo
{
    public long ChatId { get; set; }
    public int ReplyId { get; set; }
    public string? VideoUrl { get; set; }
}

public class PendingTranscode
{
    public required string FilePath { get; set; }
    public long ChatId { get; set; }
    public int ReplyId { get; set; }
    public long UserId { get; set; }
}