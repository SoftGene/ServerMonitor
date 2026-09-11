using System.Globalization;
using ServerMonitor.Infrastructure.Telegram;

namespace ServerMonitor.Tests.Telegram;

/// <summary>
/// Who the bot talks to.
/// </summary>
/// <remarks>
/// A Telegram bot is public: anyone who finds its name can message it. These are the rules that
/// keep a stranger from reading the server's metrics, and the one new rule that lets a new owner
/// finish setting the bot up without leaving Telegram.
/// </remarks>
public class TelegramAccessTests
{
    private const long Owner = 123456789;
    private const long Stranger = 987654321;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoChatConfigured_AnyoneIsToldTheirOwnChatId(string? configured)
    {
        Assert.Equal(TelegramAccess.Decision.SetupHint, TelegramAccess.Classify(configured, Stranger));
    }

    [Fact]
    public void TheConfiguredChat_IsServed()
    {
        Assert.Equal(TelegramAccess.Decision.Serve, TelegramAccess.Classify("123456789", Owner));
    }

    [Fact]
    public void AnyOtherChat_IsIgnored()
    {
        // The rule that keeps /status from answering whoever happens to find the bot.
        Assert.Equal(TelegramAccess.Decision.Ignore, TelegramAccess.Classify("123456789", Stranger));
    }

    [Fact]
    public void AStraySpaceInTheSetting_DoesNotLockTheOwnerOut()
    {
        Assert.Equal(TelegramAccess.Decision.Serve, TelegramAccess.Classify(" 123456789 ", Owner));
    }

    [Fact]
    public void AGroupChat_MatchesWhateverTheCultureWritesAMinusAs()
    {
        const long group = -1001234567890;
        var original = CultureInfo.CurrentCulture;

        try
        {
            // A culture that writes the minus sign as U+2212. Formatting the sender's ID with it
            // produces "−1001234567890", which never equals the "-1001234567890" in the setting —
            // and the bot would silently ignore the very group it was set up for.
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "−";
            CultureInfo.CurrentCulture = culture;

            Assert.Equal(TelegramAccess.Decision.Serve, TelegramAccess.Classify("-1001234567890", group));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TheSetupHint_GivesTheIdAndNothingAboutTheServer()
    {
        var hint = TelegramAccess.SetupHint(Stranger);

        Assert.Contains("987654321", hint);
        Assert.Contains("TELEGRAM_CHAT_ID", hint);

        // In setup mode anyone may receive this message, so it must carry no metrics and no
        // details of the machines being watched.
        Assert.DoesNotContain("%", hint);
        Assert.DoesNotContain("CPU", hint);
    }
}
