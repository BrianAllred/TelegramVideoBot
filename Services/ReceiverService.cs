using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramVideoBot.Abstract;
using TelegramVideoBot.Workers;

namespace TelegramVideoBot.Services;

public class ReceiverService(
    ITelegramBotClient botClient,
    UpdateHandler updateHandler,
    ILogger<ReceiverServiceBase<UpdateHandler>> logger) : ReceiverServiceBase<UpdateHandler>(botClient, updateHandler, logger)
{
    // Polling restarts after errors, but the commands only need registering once per process.
    private static bool commandsRegistered;

    public override async Task ReceiveAsync(CancellationToken stoppingToken)
    {
        // Ephemeral commands hide the user's command from the group and let the bot reply ephemerally without admin rights.
        if (!commandsRegistered)
        {
            try
            {
                await _botClient.SetMyCommands(
                [
                    new BotCommand { Command = "download", Description = "Download a video from a URL", IsEphemeral = true },
                    new BotCommand { Command = "help", Description = "Show help", IsEphemeral = true }
                ], new BotCommandScopeAllGroupChats(), cancellationToken: stoppingToken);
                commandsRegistered = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to register ephemeral bot commands");
            }
        }

        await base.ReceiveAsync(stoppingToken);
    }
}
