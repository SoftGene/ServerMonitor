using System.Net;
using ServerMonitor.Agent;

namespace ServerMonitor.Tests.AgentTests;

/// <summary>
/// Which registration failures are worth waiting out.
/// </summary>
/// <remarks>
/// Getting this wrong fails in either direction. Treat an unreachable server as permanent, and an
/// agent that boots a few seconds before its server gives up for good. Treat a refused token as
/// temporary, and the agent retries forever while looking, to systemd, perfectly healthy.
/// </remarks>
public class RegistrationFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ARefusedToken_IsPermanent(HttpStatusCode status)
    {
        var refused = new HttpRequestException("refused", null, status);

        Assert.True(RegistrationFailure.IsPermanent(refused));
    }

    [Fact]
    public void NoAnswerAtAll_IsWorthWaitingOut()
    {
        // Connection refused carries no status code. At boot this is the normal case: the agent
        // started before the server did.
        var unreachable = new HttpRequestException("Connection refused");

        Assert.False(RegistrationFailure.IsPermanent(unreachable));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void AServerSideProblem_IsWorthWaitingOut(HttpStatusCode status)
    {
        // Including 503, which is what the server answers when it has no enrollment token
        // configured: an operator can fix that without touching this machine.
        var failed = new HttpRequestException("server problem", null, status);

        Assert.False(RegistrationFailure.IsPermanent(failed));
    }

    [Fact]
    public void ATimeout_IsWorthWaitingOut()
    {
        Assert.False(RegistrationFailure.IsPermanent(new TaskCanceledException("timed out")));
    }

    [Fact]
    public void AnythingUnrecognised_IsWorthWaitingOut()
    {
        // The narrow reading is the safe one: stopping an agent that could have recovered is worse
        // than one more retry of a request that was going to fail anyway.
        Assert.False(RegistrationFailure.IsPermanent(new InvalidOperationException("empty response")));
    }
}
