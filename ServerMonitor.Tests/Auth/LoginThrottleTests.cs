using ServerMonitor.Infrastructure.Auth;

namespace ServerMonitor.Tests.Auth;

public class LoginThrottleTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static LoginThrottle Throttle() => new(maxAttempts: 5, blockFor: TimeSpan.FromMinutes(5));

    [Fact]
    public void FreshUser_IsNotBlocked()
    {
        Assert.False(Throttle().IsBlocked("pavel", Now));
    }

    [Fact]
    public void BelowTheLimit_IsNotBlocked()
    {
        var throttle = Throttle();

        for (var i = 0; i < 4; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        Assert.False(throttle.IsBlocked("pavel", Now));
    }

    [Fact]
    public void AtTheLimit_IsBlocked()
    {
        var throttle = Throttle();

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        Assert.True(throttle.IsBlocked("pavel", Now));
    }

    [Fact]
    public void BlockExpires()
    {
        var throttle = Throttle();

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        Assert.True(throttle.IsBlocked("pavel", Now.AddMinutes(4)));
        Assert.False(throttle.IsBlocked("pavel", Now.AddMinutes(6)));
    }

    [Fact]
    public void SuccessfulLogin_ResetsTheCounter()
    {
        var throttle = Throttle();

        for (var i = 0; i < 4; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        throttle.Reset("pavel");

        // After a reset the full budget of attempts is back, not just the one that was left.
        for (var i = 0; i < 4; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        Assert.False(throttle.IsBlocked("pavel", Now));
    }

    [Fact]
    public void BlockingOneUser_DoesNotAffectAnother()
    {
        var throttle = Throttle();

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        Assert.True(throttle.IsBlocked("pavel", Now));
        Assert.False(throttle.IsBlocked("anna", Now));
    }

    [Fact]
    public void UsernameComparison_IsCaseInsensitive()
    {
        var throttle = Throttle();

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure("Pavel", Now);
        }

        // Otherwise the throttle could be sidestepped by changing the case of the login.
        Assert.True(throttle.IsBlocked("pavel", Now));
    }

    [Fact]
    public void AfterTheBlockExpires_TheCounterStartsOver()
    {
        var throttle = Throttle();

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure("pavel", Now);
        }

        var later = Now.AddMinutes(6);

        // The block has expired; a single fresh failure must not lock it again.
        throttle.RecordFailure("pavel", later);

        Assert.False(throttle.IsBlocked("pavel", later));
    }
}
