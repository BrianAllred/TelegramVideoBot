using Telegram.Bot;
using TelegramVideoBot.Abstract;
using TelegramVideoBot.Workers;

namespace TelegramVideoBot.Services;

public class ReceiverService : ReceiverServiceBase<UpdateHandler>
{
    public ReceiverService(
        ITelegramBotClient botClient,
        UpdateHandler updateHandler,
        ILogger<ReceiverServiceBase<UpdateHandler>> logger)
        : base(botClient, updateHandler, logger)
    {
    }
}