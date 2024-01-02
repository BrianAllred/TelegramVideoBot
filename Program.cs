using Telegram.Bot;
using TelegramVideoBot.Services;
using TelegramVideoBot.Utilities;
using TelegramVideoBot.Workers;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = new EnvironmentConfig();

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<YtDlp>();
        builder.Services.AddHttpClient("telegram_bot_client")
            .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
            {
                var options = new TelegramBotClientOptions(config.TelegramBotToken, config.TelegramApiServer);
                return new TelegramBotClient(options, httpClient);
            });

        builder.Services.AddScoped<UpdateHandler>();
        builder.Services.AddScoped<ReceiverService>();
        builder.Services.AddHostedService<PollingService>();

        var app = builder.Build();

        app.Services.GetRequiredService<YtDlp>();

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}