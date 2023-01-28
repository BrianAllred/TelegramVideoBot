namespace TelegramVideoBot.Utilities
{
    public class EnvironmentConfig
    {
        public string? TelegramBotToken => Environment.GetEnvironmentVariable("TG_BOT_TOKEN");
        public string? TelegramBotName => Environment.GetEnvironmentVariable("TG_BOT_NAME");
    }
}