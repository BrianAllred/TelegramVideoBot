using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramVideoBot.Utilities;

public static class Extensions
{
    public static long SenderUserId(this Message message) => message.From?.Id ?? -1;

    // Ephemeral messages have MessageId 0, and a normal message can't reply to them.
    public static ReplyParameters? NormalReply(this Message message) => message.MessageId == 0 ? null : message.MessageId;

    // A message that isn't a reply lands in a forum's General topic unless it names its topic.
    public static int? TopicId(this Message message) => message.IsTopicMessage ? message.MessageThreadId : null;

    // Tags the user by @username, or by a mention link when they have no username.
    public static string MentionHtml(this User user) =>
        user.Username is { } username ? $"@{username}" : $"<a href=\"tg://user?id={user.Id}\">{System.Net.WebUtility.HtmlEncode(user.FirstName)}</a>";

    // Inside MarkdownV2 code spans, only ` and \ need escaping.
    public static string MarkdownV2Code(string text) => $"`{text.Replace(@"\", @"\\").Replace("`", @"\`")}`";

    // Telegram allows 15 s; the margin covers clock skew.
    private static readonly TimeSpan EphemeralReplyWindow = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan AdminCheckLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<long, (bool IsAdmin, DateTime CheckedAt)> botAdminChecks = new();

    /// <summary>
    /// Replies to <paramref name="trigger"/> with a text message only its sender can see when the chat is a group.
    /// Telegram allows that within 15 seconds of an ephemeral command, or at any time when the bot is an admin.
    /// Otherwise, or when Telegram refuses, it sends a private message.
    /// </summary>
    public static async Task ReplyPrivately(
        this ITelegramBotClient client,
        Message trigger,
        string text,
        ILogger logger,
        ParseMode parseMode = default,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        if (await TrySendEphemeral(client, trigger, text, logger, parseMode, replyMarkup, cancellationToken)) return;

        if (trigger.Chat.Type is ChatType.Group or ChatType.Supergroup)
        {
            try
            {
                await client.SendMessage(trigger.SenderUserId(), text, parseMode, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
                return;
            }
            // Bots can't message users who never started a chat with them.
            catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
            {
                logger.LogInformation("Can't message user {UserId} privately ({Error}), sending a normal message instead", trigger.SenderUserId(), ex.Message);
            }
        }

        await client.SendMessage(trigger.Chat.Id, text, parseMode, trigger.NormalReply(), replyMarkup: replyMarkup, messageThreadId: trigger.TopicId(), cancellationToken: cancellationToken);
    }

    // Returns false when the chat isn't a group or Telegram doesn't allow an ephemeral message.
    private static async Task<bool> TrySendEphemeral(
        ITelegramBotClient client,
        Message trigger,
        string text,
        ILogger logger,
        ParseMode parseMode,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken cancellationToken)
    {
        if (trigger.Chat.Type is not (ChatType.Group or ChatType.Supergroup)) return false;

        var chatId = trigger.Chat.Id;
        ReplyParameters? ephemeralReply = trigger.EphemeralMessageId is { } ephemeralId && DateTime.UtcNow - trigger.Date < EphemeralReplyWindow
            ? new ReplyParameters { EphemeralMessageId = ephemeralId }
            : null;

        if (ephemeralReply is null && !await BotIsAdmin(client, chatId, logger, cancellationToken)) return false;

        try
        {
            await client.SendMessage(chatId, text, parseMode, ephemeralReply ?? trigger.NormalReply(), replyMarkup, messageThreadId: trigger.TopicId(),
                ephemeralMessageParameters: new EphemeralMessageParameters { ReceiverUserId = trigger.SenderUserId() },
                cancellationToken: cancellationToken);
            return true;
        }
        // Only a refusal falls back. Flood limits, bad markup or removal from the chat would fail a normal message too.
        catch (ApiRequestException ex) when (ex.ErrorCode == 400 && !ex.Message.Contains("can't parse entities"))
        {
            logger.LogInformation("Ephemeral message refused ({Error})", ex.Message);
            // The bot may have lost admin rights since the last check.
            if (ephemeralReply is null) botAdminChecks.TryRemove(chatId, out _);
            return false;
        }
    }

    private static async Task<bool> BotIsAdmin(ITelegramBotClient client, long chatId, ILogger logger, CancellationToken cancellationToken)
    {
        if (botAdminChecks.TryGetValue(chatId, out var check) && DateTime.UtcNow - check.CheckedAt < AdminCheckLifetime) return check.IsAdmin;

        try
        {
            var isAdmin = (await client.GetChatMember(chatId, client.BotId, cancellationToken)).Status == ChatMemberStatus.Administrator;
            botAdminChecks[chatId] = (isAdmin, DateTime.UtcNow);
            return isAdmin;
        }
        catch (ApiRequestException ex)
        {
            logger.LogWarning("Couldn't check the bot's admin rights in {ChatId} ({Error})", chatId, ex.Message);
            return false;
        }
    }
}
