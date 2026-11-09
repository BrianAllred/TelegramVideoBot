using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using FFMpegCore.Exceptions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramVideoBot.Services;
using static TelegramVideoBot.Utilities.Enums;
using static TelegramVideoBot.Utilities.Extensions;

namespace TelegramVideoBot.Workers;

public class DownloadManager
{
    private readonly Channel<DownloadInfo> downloads;
    private readonly long userId;
    private readonly ITelegramBotClient client;
    private readonly ILogger logger;
    private readonly int fileSizeLimit;
    private readonly S3StorageService s3;

    public DownloadManager(ITelegramBotClient client, long userId, int queueLimit, int fileSizeLimit, S3StorageService s3, ILogger logger)
    {
        this.client = client;
        this.userId = userId;
        this.logger = logger;
        this.fileSizeLimit = fileSizeLimit;
        this.s3 = s3;

        // The bounded channel enforces the queue limit, and its single reader keeps each user's downloads sequential.
        downloads = Channel.CreateBounded<DownloadInfo>(queueLimit);
        _ = Task.Run(ProcessDownloads);
    }

    public DownloadQueueStatus QueueDownload(DownloadInfo download)
    {
        if (!Uri.TryCreate(download.VideoUrl, UriKind.Absolute, out var uriResult) || !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)) return DownloadQueueStatus.InvalidUrl;

        return downloads.Writer.TryWrite(download) ? DownloadQueueStatus.Success : DownloadQueueStatus.QueueFull;
    }

    private async Task ProcessDownloads()
    {
        await foreach (var download in downloads.Reader.ReadAllAsync())
        {
            await Download(download);
        }
    }

    // Must not throw: that would end ProcessDownloads and strand the user's queue.
    private async Task Download(DownloadInfo download)
    {
        var trigger = download.Trigger;
        // Every file this download creates (yt-dlp parts, the video, transcode output) starts with this prefix.
        var filePrefix = $"{userId}_{Guid.NewGuid().ToString("N")[..8]}";
        var awaitingChoice = false;

        try
        {
            var downloadProcInfo = new ProcessStartInfo("yt-dlp")
            {
                // adding remote-components here as per https://github.com/yt-dlp/yt-dlp/wiki/EJS
                // NOTE that this adds a dependency on deno or similar EJS runtime
                Arguments = $"-f \"bv*+ba/b\" --remote-components ejs:github -o {filePrefix}.%(ext)s {download.VideoUrl}",
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
                await client.ReplyEphemeral(trigger, replyBuilder.ToString(), logger, ParseMode.MarkdownV2);
                return;
            }

            var filePath = Directory.GetFiles("./").First(file => file.StartsWith($"./{filePrefix}"))[2..];
            var needTranscode = NeedTranscode(new FileInfo(filePath));

            if (needTranscode && s3.IsEnabled)
            {
                var pendingId = Guid.NewGuid().ToString("N")[..8];
                PendingTranscodeChoices[pendingId] = new PendingTranscode
                {
                    FilePath = filePath,
                    Trigger = trigger
                };
                awaitingChoice = true;
                _ = ExpirePendingChoice(pendingId);

                var keyboard = new InlineKeyboardMarkup(
                [
                    [
                        InlineKeyboardButton.WithCallbackData("Transcode for Telegram", $"transcode:{pendingId}"),
                        InlineKeyboardButton.WithCallbackData("Get original", $"original:{pendingId}")
                    ]
                ]);

                await client.ReplyEphemeral(trigger, "This video needs transcoding to send via Telegram\\.\n\nTranscoding will reduce quality, especially for large/long videos\\.\n\nWhat would you like to do?", logger, ParseMode.MarkdownV2, keyboard);
                return;
            }

            if (needTranscode)
            {
                await client.ReplyEphemeral(trigger, $"Video {MarkdownV2Code(download.VideoUrl)} must be transcoded, please wait\\.", logger, ParseMode.MarkdownV2);
                filePath = await TranscodeVideo(filePath);
            }

            await SendVideo(trigger, filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            var replyBuilder = new StringBuilder($"Sorry, something went wrong downloading {MarkdownV2Code(download.VideoUrl)}\\. I can't \\(currently\\!\\) access private videos, so please make sure it's available to the public\\.\n\n");
            replyBuilder.AppendLine("If that's not the problem, contact [my creator](tg://user?id=247371329) for more help\\.");
            await ReplyError(trigger, replyBuilder.ToString(), ParseMode.MarkdownV2);
        }
        finally
        {
            // A file waiting for the user's choice now belongs to that choice.
            if (!awaitingChoice) DeleteFiles(filePrefix);
        }
    }

    private async Task SendVideo(Message trigger, string filePath)
    {
        using var videoStream = File.OpenRead(filePath);
        var inputFile = InputFile.FromStream(videoStream);
        var analysis = await FFProbe.AnalyseAsync(filePath);
        await client.SendVideo(trigger.Chat.Id, inputFile, replyParameters: trigger.NormalReply(), messageThreadId: trigger.TopicId(), height: analysis.PrimaryVideoStream!.Height, width: analysis.PrimaryVideoStream!.Width);
    }

    /// <summary>
    /// Runs in the background so a long transcode or upload doesn't hold up other users' updates.
    /// The caller must have removed <paramref name="pending"/> from <see cref="PendingTranscodeChoices"/>.
    /// For the original, the caller must also have checked that the bot can message the user privately.
    /// </summary>
    public void HandleTranscodeChoice(PendingTranscode pending, bool transcode) => _ = Task.Run(async () =>
    {
        try
        {
            if (transcode)
            {
                await SendVideo(pending.Trigger, await TranscodeVideo(pending.FilePath));
                return;
            }

            var objectKey = $"{userId}/{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Path.GetFileName(pending.FilePath)}";
            var presignedUrl = await s3.UploadAndGetPresignedUrl(pending.FilePath, objectKey);
            var expiryDays = s3.PresignExpiryDays;
            // Only ever sent privately: anyone with the link can download the video.
            await client.SendMessage(userId, $"Here's your original video. This link expires in {expiryDays} day{(expiryDays != 1 ? "s" : "")}:\n{presignedUrl}",
                replyParameters: pending.Trigger.Chat.Id == userId ? pending.Trigger.NormalReply() : null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle transcode choice");
            await ReplyError(pending.Trigger, "Sorry, something went wrong processing your choice.");
        }
        finally
        {
            DeleteFiles(Path.GetFileNameWithoutExtension(pending.FilePath));
        }
    });

    // Error replies must not throw, or they'd hide the original error.
    private async Task ReplyError(Message trigger, string text, ParseMode parseMode = default)
    {
        try
        {
            await client.ReplyEphemeral(trigger, text, logger, parseMode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send error reply");
        }
    }

    public static readonly ConcurrentDictionary<string, PendingTranscode> PendingTranscodeChoices = new();

    // The prompt may be ephemeral and vanish or never arrive, so unanswered choices must not keep their file forever.
    private static readonly TimeSpan PendingChoiceExpiry = TimeSpan.FromHours(1);

    private async Task ExpirePendingChoice(string pendingId)
    {
        await Task.Delay(PendingChoiceExpiry);
        // TryRemove loses to UpdateHandler if the user already chose, so an in-use file is never deleted here.
        if (PendingTranscodeChoices.TryRemove(pendingId, out var pending))
            DeleteFiles(Path.GetFileNameWithoutExtension(pending.FilePath));
    }

    private void DeleteFiles(string filePrefix)
    {
        foreach (var file in Directory.GetFiles(".", $"{filePrefix}*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete {File}", file);
            }
        }
    }

    // Pending choices only live in memory, so download files left over from a previous run are orphaned.
    public static void DeleteLeftoverFiles()
    {
        foreach (var file in Directory.GetFiles(".").Where(file => Regex.IsMatch(Path.GetFileName(file), @"^\d+_[0-9a-f]{8}[._]")))
        {
            File.Delete(file);
        }
    }

    // https://unix.stackexchange.com/questions/520597/how-to-reduce-the-size-of-a-video-to-a-target-size
    // Returns the path of the transcoded video.
    private async Task<string> TranscodeVideo(string filePath)
    {
        var newFilePath = $"{Path.GetFileNameWithoutExtension(filePath)}_new.mp4";

        var targetSizeInKiloBits = (fileSizeLimit - 5) * 1000 * 8;
        var mediaInfo = await FFProbe.AnalyseAsync(filePath);
        var totalBitRate = (targetSizeInKiloBits / mediaInfo.Duration.TotalSeconds) + 1;
        var audioBitRate = 128;
        var videoBitRate = (int)(totalBitRate - audioBitRate);

        try
        {
            if (File.Exists(newFilePath))
            {
                File.Delete(newFilePath);
            }

            await FFMpegArguments.FromFileInput(filePath, false, options => options
                        .WithHardwareAcceleration())
                        .OutputToFile(newFilePath, false, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithArgument(new CustomArgument($"-maxrate:v {videoBitRate}k"))
                        .WithArgument(new CustomArgument($"-bufsize:v {targetSizeInKiloBits * 1000 / 20}"))
                        .WithFastStart())
                        .NotifyOnStandardError((err) => { logger.LogError(err); })
                        .NotifyOnStandardOutput((output) => { logger.LogInformation(output); })
                        .ProcessAsynchronously();
        }
        catch (FFMpegException ex)
        {
            logger.LogError(ex, ex.Message);
        }

        if (Path.GetExtension(filePath) != ".mp4")
        {
            File.Delete(filePath);
            filePath = $"{Path.GetFileNameWithoutExtension(filePath)}.mp4";
        }

        File.Move(newFilePath, filePath, true);
        return filePath;
    }

    // Check if video needs to be transcoded.
    // NOTE that ALL (tested) iOS and some Mac devices need videos to be h264/aac
    // Also check file size and container format
    private bool NeedTranscode(FileInfo fileInfo)
    {
        var mediaInfo = FFProbe.Analyse(fileInfo.Name);
        if (mediaInfo == null || mediaInfo.PrimaryAudioStream == null || mediaInfo.PrimaryVideoStream == null)
        {
            throw new Exception("Unable to analyze video metadata");
        }

        return fileInfo.Extension != ".mp4"
        || mediaInfo.PrimaryVideoStream.CodecName != "h264"
        || mediaInfo.PrimaryAudioStream.CodecName != "aac"
        || fileInfo.Length > fileSizeLimit * 1000 * 1000;
    }
}
