using Telegram.Bot;
using TelegramVideoBot.Abstract;
using TelegramVideoBot.Workers;

namespace TelegramVideoBot.Services;

public class ReceiverService(
    ITelegramBotClient botClient,
    UpdateHandler updateHandler,
    ILogger<ReceiverServiceBase<UpdateHandler>> logger) : ReceiverServiceBase<UpdateHandler>(botClient, updateHandler, logger)
{
}