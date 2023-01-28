using System.Diagnostics;

namespace TelegramVideoBot.Workers
{
    public static class YtDlp
    {
        public static void Update()
        {
            var updateStartInfo = new ProcessStartInfo("python3")
            {
                Arguments = "-m pip install --upgrade git+https://github.com/yt-dlp/yt-dlp.git@release",
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var updateProcess = new Process { StartInfo = updateStartInfo };

            updateProcess.OutputDataReceived += (sender, args) => { Console.WriteLine(args.Data); };
            updateProcess.ErrorDataReceived += (sender, args) => { Console.WriteLine(args.Data); };

            updateProcess.Start();
            updateProcess.BeginErrorReadLine();
            updateProcess.BeginOutputReadLine();

            updateProcess.WaitForExit();
        }
    }
}