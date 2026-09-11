using System.Globalization;

namespace ServerMonitor.Infrastructure.Telegram;

/// <summary>
/// Decides what the bot may do with a message, based on which chat it came from.
/// </summary>
/// <remarks>
/// Kept apart from the bot service so it can be tested without Telegram. It is also the one place
/// the bot's security rests on: a bot is public — anyone who finds its name can message it — so
/// this is what stands between a stranger and the server's metrics.
/// </remarks>
public static class TelegramAccess
{
    public enum Decision
    {
        /// <summary>A chat other than the configured one. Say nothing.</summary>
        Ignore,

        /// <summary>No chat is configured yet. Tell the sender their chat ID and nothing more.</summary>
        SetupHint,

        /// <summary>The configured chat. Answer commands.</summary>
        Serve
    }

    /// <param name="configuredChatId">Telegram:ChatId from configuration, possibly empty.</param>
    /// <param name="senderChatId">The chat the message arrived from.</param>
    public static Decision Classify(string? configuredChatId, long senderChatId)
    {
        // Without a chat ID the bot used not to start at all, which left a new user no way to
        // learn theirs except a raw Bot API URL — one that returns nothing as soon as the server is
        // polling with the same token. Answering with the ID closes that loop from inside Telegram,
        // and discloses nothing: the sender learns their own ID, not a single fact about the server.
        if (string.IsNullOrWhiteSpace(configuredChatId))
        {
            return Decision.SetupHint;
        }

        // Invariant culture, because a group's ID is negative and some cultures write the minus
        // sign as U+2212 rather than a hyphen. Under such a culture the configured group would
        // never match and the bot would ignore its own owners. Trimmed, because a stray space in
        // an .env file is invisible and should not lock anyone out.
        var sender = senderChatId.ToString(CultureInfo.InvariantCulture);

        return string.Equals(sender, configuredChatId.Trim(), StringComparison.Ordinal)
            ? Decision.Serve
            : Decision.Ignore;
    }

    /// <summary>The reply in setup mode: the sender's chat ID and where to put it.</summary>
    public static string SetupHint(long chatId) =>
        "🔧 <b>ServerMonitor setup</b>\n\n" +
        $"This chat's ID is <code>{chatId.ToString(CultureInfo.InvariantCulture)}</code>.\n\n" +
        "Put it into <code>TELEGRAM_CHAT_ID</code> in the server's <code>.env</code> and restart with " +
        "<code>docker compose up -d</code>. Until then this bot answers nothing else.";
}
