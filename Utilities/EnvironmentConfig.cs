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

    // S3-compatible storage (works with AWS S3, Garage, MinIO, Backblaze B2, etc.)
    public string? S3Endpoint => Environment.GetEnvironmentVariable("S3_ENDPOINT");
    public string? S3AccessKey => Environment.GetEnvironmentVariable("S3_ACCESS_KEY");
    public string? S3SecretKey => Environment.GetEnvironmentVariable("S3_SECRET_KEY");
    public string? S3Bucket => Environment.GetEnvironmentVariable("S3_BUCKET");
    public string S3Region => Environment.GetEnvironmentVariable("S3_REGION") ?? "us-east-1";
    public bool S3ForcePathStyle => bool.Parse(Environment.GetEnvironmentVariable("S3_FORCE_PATH_STYLE") ?? "false");
    public int S3PresignExpiryDays => int.Parse(Environment.GetEnvironmentVariable("S3_PRESIGN_EXPIRY_DAYS") ?? "3");
    public bool S3DisablePayloadSigning => bool.Parse(Environment.GetEnvironmentVariable("S3_DISABLE_PAYLOAD_SIGNING") ?? "false");
    public bool S3Enabled => !string.IsNullOrEmpty(S3AccessKey) && !string.IsNullOrEmpty(S3SecretKey) && !string.IsNullOrEmpty(S3Bucket);
}