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

    // Inside MarkdownV2 code spans, only ` and \ need escaping.
    public static string MarkdownV2Code(string text) => $"`{text.Replace(@"\", @"\\").Replace("`", @"\`")}`";

    // Telegram allows 15 s; the margin covers clock skew.
    private static readonly TimeSpan EphemeralReplyWindow = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan AdminCheckLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<long, (bool IsAdmin, DateTime CheckedAt)> botAdminChecks = new();

    /// <summary>
    /// Replies to <paramref name="trigger"/> with a text message only its sender can see when the chat is a group.
    /// Telegram allows that within 15 seconds of an ephemeral command, or at any time when the bot is an admin.
    /// Otherwise, or when Telegram refuses, it sends a normal message.
    /// </summary>
    public static async Task ReplyEphemeral(
        this ITelegramBotClient client,
        Message trigger,
        string text,
        ILogger logger,
        ParseMode parseMode = default,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        var chatId = trigger.Chat.Id;
        if (trigger.Chat.Type is ChatType.Group or ChatType.Supergroup)
        {
            ReplyParameters? ephemeralReply = trigger.EphemeralMessageId is { } ephemeralId && DateTime.UtcNow - trigger.Date < EphemeralReplyWindow
                ? new ReplyParameters { EphemeralMessageId = ephemeralId }
                : null;

            if (ephemeralReply is not null || await BotIsAdmin(client, chatId, logger, cancellationToken))
            {
                try
                {
                    await client.SendMessage(chatId, text, parseMode, ephemeralReply ?? trigger.NormalReply(), replyMarkup, messageThreadId: trigger.TopicId(),
                        ephemeralMessageParameters: new EphemeralMessageParameters { ReceiverUserId = trigger.SenderUserId() },
                        cancellationToken: cancellationToken);
                    return;
                }
                // Only a refusal falls back. Flood limits, bad markup or removal from the chat would fail a normal message too.
                catch (ApiRequestException ex) when (ex.ErrorCode == 400 && !ex.Message.Contains("can't parse entities"))
                {
                    logger.LogInformation("Ephemeral message refused ({Error}), sending a normal message instead", ex.Message);
                    // The bot may have lost admin rights since the last check.
                    if (ephemeralReply is null) botAdminChecks.TryRemove(chatId, out _);
                }
            }
        }

        await client.SendMessage(chatId, text, parseMode, trigger.NormalReply(), replyMarkup, messageThreadId: trigger.TopicId(), cancellationToken: cancellationToken);
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
