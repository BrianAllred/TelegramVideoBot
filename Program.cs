using TelegramVideoBot.Utilities;
using TelegramVideoBot.Workers;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = new EnvironmentConfig();

        if (config.UpdateYtDlpOnStart)
            YtDlp.Update();

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSingleton(config);
        builder.Services.AddHostedService<TelegramVideoBot.Workers.Telegram>();

        var app = builder.Build();

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}