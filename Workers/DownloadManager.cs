using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Exceptions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static TelegramVideoBot.Utilities.Enums;

namespace TelegramVideoBot.Workers;

public class DownloadManager
{
    private readonly ConcurrentQueue<DownloadInfo> downloads = new();
    private readonly long userId;
    private readonly ITelegramBotClient client;
    private readonly int queueLimit;

    private bool downloading;

    public DownloadManager(ITelegramBotClient client, long userId, int queueLimit)
    {
        this.userId = userId;
        this.client = client;
        this.queueLimit = queueLimit;
    }

    public DownloadQueueStatus QueueDownload(DownloadInfo download)
    {
        if (downloads.Count >= queueLimit) return DownloadQueueStatus.QueueFull;

        if (!Uri.TryCreate(download.VideoUrl, UriKind.Absolute, out var uriResult) || !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)) return DownloadQueueStatus.InvalidUrl;

        downloads.Enqueue(download);

        _ = Task.Run(() => StartDownloads());

        return DownloadQueueStatus.Success;
    }

    private async Task StartDownloads()
    {
        if (downloading) return;

        while (downloads.TryDequeue(out var download))
        {
            try
            {
                downloading = true;
                var filePath = Directory.GetFiles("./").Where(file => file.StartsWith($"./{userId}")).FirstOrDefault();
                filePath = filePath?[2..] ?? "";
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                var downloadProcInfo = new ProcessStartInfo("yt-dlp")
                {
                    Arguments = $"-f webm+bestaudio/mp4+bestaudio/mkv+bestaudio -S +size -o {userId}.%(ext)s {download.VideoUrl}",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                var downloadProc = new Process
                {
                    StartInfo = downloadProcInfo,
                    EnableRaisingEvents = true
                };

                var output = string.Empty;
                downloadProc.ErrorDataReceived += (sender, o) =>
                {
                    output += o.Data;
                    Console.WriteLine(output);
                };
                downloadProc.OutputDataReceived += (sender, o) =>
                {
                    output += o.Data;
                    Console.WriteLine(output);
                };

                downloadProc.Start();
                downloadProc.BeginErrorReadLine();
                downloadProc.BeginOutputReadLine();
                await downloadProc.WaitForExitAsync();

                if (output.Contains("Unsupported URL"))
                {
                    var replyBuilder = new StringBuilder("Failed to find video, are you sure this website/format is supported?\n\n");
                    replyBuilder.AppendLine("Please check the list of supported sites [here](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md)\\.");
                    await client.SendTextMessageAsync(download.ChatId, replyBuilder.ToString(), parseMode: ParseMode.MarkdownV2, replyToMessageId: download.ReplyId);
                    continue;
                }

                filePath = Directory.GetFiles("./").Where(file => file.StartsWith($"./{userId}")).First()[2..];

                var videoFileInfo = new FileInfo(filePath);
                if (videoFileInfo.Length > 50 * 1000 * 1000)
                {
                    await client.SendTextMessageAsync(download.ChatId, $"Video `{download.VideoUrl}` is larger than 50MB and requires further compression, please wait\\.", parseMode: ParseMode.MarkdownV2, replyToMessageId: download.ReplyId);
                    CompressVideo(filePath);
                }

                using var videoStream = System.IO.File.OpenRead(filePath);

                if (videoStream == null)
                {
                    throw new Exception();
                }
                else
                {
                    var inputFile = new InputFile(videoStream);
                    await client.SendVideoAsync(download.ChatId, inputFile, replyToMessageId: download.ReplyId);
                    System.IO.File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                var replyBuilder = new StringBuilder($"Sorry, something went wrong downloading `{download.VideoUrl}`\\. I can't \\(currently\\!\\) access private videos, so please make sure it's available to the public\\.\n\n");
                replyBuilder.AppendLine("If that's not the problem, contact [my creator](tg://user?id=247371329) for more help\\.");
                await client.SendTextMessageAsync(download.ChatId, replyBuilder.ToString(), parseMode: ParseMode.MarkdownV2, replyToMessageId: download.ReplyId);
                Console.WriteLine(ex);
            }
            finally
            {
                downloading = false;
            }
        }
    }

    // https://unix.stackexchange.com/questions/520597/how-to-reduce-the-size-of-a-video-to-a-target-size
    private static void CompressVideo(string filePath)
    {
        var newFilePath = $"{Path.GetFileNameWithoutExtension(filePath)}_new{Path.GetExtension(filePath)}";

        var targetSizeInKiloBits = 45 * 1000 * 8; // 45MB target size, API limit is 50
        var mediaInfo = FFProbe.Analyse(filePath);
        var totalBitRate = (targetSizeInKiloBits / mediaInfo.Duration.TotalSeconds) + 1;
        var audioBitRate = 128;
        var videoBitRate = (int)(totalBitRate - audioBitRate);

        try
        {
            if (System.IO.File.Exists(newFilePath))
            {
                System.IO.File.Delete(newFilePath);
            }

            FFMpegArguments.FromFileInput(filePath, false, options => options
                        .WithArgument(new CustomArgument("-hwaccel auto")))
                        .OutputToFile(newFilePath, false, options => options
                        .WithVideoBitrate(videoBitRate)
                        .WithAudioBitrate(audioBitRate)
                        .WithArgument(new CustomArgument($"-maxrate:v {videoBitRate}k"))
                        .WithArgument(new CustomArgument($"-bufsize:v {targetSizeInKiloBits * 1000 / 20}"))
                        .WithFastStart())
                        .NotifyOnError((err) => { Console.WriteLine(err); })
                        .NotifyOnOutput((output) => { Console.WriteLine(output); })
                        .ProcessSynchronously();
        }
        catch (FFMpegException ex)
        {
            Console.WriteLine(ex);
        }

        System.IO.File.Move(newFilePath, filePath, true);
    }
}
