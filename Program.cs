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
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton(config);
        builder.Services.AddHostedService<TelegramVideoBot.Workers.Telegram>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}