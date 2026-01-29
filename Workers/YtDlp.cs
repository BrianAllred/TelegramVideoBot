using System.Diagnostics;
using TelegramVideoBot.Utilities;

namespace TelegramVideoBot.Workers;

public class YtDlp
{
    public YtDlp(EnvironmentConfig config, ILogger<YtDlp> logger)
    {
        if (config.UpdateYtDlpOnStart)
        {
            var updateStartInfo = new ProcessStartInfo("python3")
            {
                Arguments = $"-m pip install --force-reinstall --break-system-packages \"yt-dlp[default] @ git+https://github.com/yt-dlp/yt-dlp.git@{config.YtDlpUpdateBranch}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var updateProcess = new Process { StartInfo = updateStartInfo };

            updateProcess.OutputDataReceived += (sender, args) => { logger.LogInformation(args.Data); };
            updateProcess.ErrorDataReceived += (sender, args) => { logger.LogError(args.Data); };

            updateProcess.Start();
            updateProcess.BeginErrorReadLine();
            updateProcess.BeginOutputReadLine();

            updateProcess.WaitForExit();
        }
    }
}
