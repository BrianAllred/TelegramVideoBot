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

public class DownloadManager(ITelegramBotClient client, long userId, int queueLimit, int fileSizeLimit, ILogger logger)
{
    private readonly ConcurrentQueue<DownloadInfo> downloads = new();
    private readonly long userId = userId;
    private readonly ITelegramBotClient client = client;
    private readonly int queueLimit = queueLimit;
    private readonly ILogger logger = logger;
    private readonly int fileSizeLimit = fileSizeLimit;

    private bool downloading;

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
                    Arguments = $"-f \"bv*+ba/b\" -S \"filesize~50M\" -o {userId}.%(ext)s {download.VideoUrl}",
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
                    logger.LogError(o.Data);
                };
                downloadProc.OutputDataReceived += (sender, o) =>
                {
                    output += o.Data;
                    logger.LogInformation(o.Data);
                };

                downloadProc.Start();
                downloadProc.BeginErrorReadLine();
                downloadProc.BeginOutputReadLine();
                await downloadProc.WaitForExitAsync();

                if (output.Contains("Unsupported URL"))
                {
                    var replyBuilder = new StringBuilder("Failed to find video, are you sure this website/format is supported?\n\n");
                    replyBuilder.AppendLine("Please check the list of supported sites [here](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md)\\.");
                    await client.SendMessage(download.ChatId, replyBuilder.ToString(), parseMode: ParseMode.MarkdownV2, replyParameters: download.ReplyId);
                    continue;
                }

                filePath = Directory.GetFiles("./").Where(file => file.StartsWith($"./{userId}")).First()[2..];

                var videoFileInfo = new FileInfo(filePath);
                if (videoFileInfo.Length > fileSizeLimit * 1000 * 1000)
                {
                    await client.SendMessage(download.ChatId, $"Video `{download.VideoUrl}` is larger than 50MB and requires further compression, please wait\\.", parseMode: ParseMode.MarkdownV2, replyParameters: download.ReplyId);
                    CompressVideo(filePath);
                    filePath = $"{Path.GetFileNameWithoutExtension(filePath)}.mp4";
                }
                else if (videoFileInfo.Extension != ".mp4") // This is an "else" because the compression above will set the correct extension
                {
                    await client.SendMessage(download.ChatId, $"Video `{download.VideoUrl}` must be transcoded, please wait\\.", parseMode: ParseMode.MarkdownV2, replyParameters: download.ReplyId);
                    CompressVideo(filePath);
                    filePath = $"{Path.GetFileNameWithoutExtension(filePath)}.mp4";
                }

                using var videoStream = System.IO.File.OpenRead(filePath);

                if (videoStream == null)
                {
                    throw new Exception();
                }
                else
                {
                    var inputFile = InputFile.FromStream(videoStream);
                    var analysis = await FFProbe.AnalyseAsync(filePath);
                    await client.SendVideo(download.ChatId, inputFile, replyParameters: download.ReplyId, height: analysis.PrimaryVideoStream!.Height, width: analysis.PrimaryVideoStream!.Width);
                    System.IO.File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                var replyBuilder = new StringBuilder($"Sorry, something went wrong downloading `{download.VideoUrl}`\\. I can't \\(currently\\!\\) access private videos, so please make sure it's available to the public\\.\n\n");
                replyBuilder.AppendLine("If that's not the problem, contact [my creator](tg://user?id=247371329) for more help\\.");
                await client.SendMessage(download.ChatId, replyBuilder.ToString(), parseMode: ParseMode.MarkdownV2, replyParameters: download.ReplyId);
                logger.LogError(ex, ex.Message);
            }
            finally
            {
                downloading = false;
            }
        }
    }

    // https://unix.stackexchange.com/questions/520597/how-to-reduce-the-size-of-a-video-to-a-target-size
    private void CompressVideo(string filePath)
    {
        var newFilePath = $"{Path.GetFileNameWithoutExtension(filePath)}_new.mp4";

        var targetSizeInKiloBits = (fileSizeLimit - 5) * 1000 * 8;
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
                        .WithHardwareAcceleration())
                        .OutputToFile(newFilePath, false, options => options
                        .WithVideoBitrate(videoBitRate)
                        .WithAudioBitrate(audioBitRate)
                        .WithArgument(new CustomArgument($"-maxrate:v {videoBitRate}k"))
                        .WithArgument(new CustomArgument($"-bufsize:v {targetSizeInKiloBits * 1000 / 20}"))
                        .WithFastStart())
                        .NotifyOnError((err) => { logger.LogError(err); })
                        .NotifyOnOutput((output) => { logger.LogInformation(output); })
                        .ProcessSynchronously();
        }
        catch (FFMpegException ex)
        {
            logger.LogError(ex, ex.Message);
        }

        if (Path.GetExtension(filePath) != ".mp4")
        {
            System.IO.File.Delete(filePath);
            filePath = $"{Path.GetFileNameWithoutExtension(filePath)}.mp4";
        }

        System.IO.File.Move(newFilePath, filePath, true);
    }
}
