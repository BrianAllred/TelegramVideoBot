namespace TelegramVideoBot.Utilities;

public class EnvironmentConfig
{
    public string TelegramBotToken => Environment.GetEnvironmentVariable("TG_BOT_TOKEN") ?? string.Empty;
    public string TelegramBotName => Environment.GetEnvironmentVariable("TG_BOT_NAME") ?? string.Empty;
    public bool UpdateYtDlpOnStart => bool.Parse(Environment.GetEnvironmentVariable("UPDATE_YTDLP_ON_START") ?? "false");
    public int DownloadQueueLimit => int.Parse(Environment.GetEnvironmentVariable("DOWNLOAD_QUEUE_LIMIT") ?? "5");
}