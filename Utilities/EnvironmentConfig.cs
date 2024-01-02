namespace TelegramVideoBot.Utilities;

public class EnvironmentConfig
{
    public string TelegramBotToken => Environment.GetEnvironmentVariable("TG_BOT_TOKEN") ?? string.Empty;
    public string TelegramBotName => Environment.GetEnvironmentVariable("TG_BOT_NAME") ?? string.Empty;
    public bool UpdateYtDlpOnStart => bool.Parse(Environment.GetEnvironmentVariable("UPDATE_YTDLP_ON_START") ?? "false");
    public int DownloadQueueLimit => int.Parse(Environment.GetEnvironmentVariable("DOWNLOAD_QUEUE_LIMIT") ?? "5");
    public string YtDlpUpdateBranch => Environment.GetEnvironmentVariable("YTDLP_UPDATE_BRANCH") ?? "release";
    public string? TelegramApiServer => Environment.GetEnvironmentVariable("TG_API_SERVER");

    // The Telegram public API limit is 50MB
    public int FileSizeLimit => int.Parse(Environment.GetEnvironmentVariable("FILE_SIZE_LIMIT") ?? "50");
}