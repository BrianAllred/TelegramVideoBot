using TelegramVideoBot.Abstract;

namespace TelegramVideoBot.Services;

public class PollingService(IServiceProvider serviceProvider, ILogger<PollingService> logger) : PollingServiceBase<ReceiverService>(serviceProvider, logger)
{
}