using Telegram.Bot.Types;

namespace TelegramVideoBot.Utilities;

public static class Extensions
{
    public static long SenderUserId(this Message message)
    {
        return message.From?.Id ?? -1;
    }
}