using ServerMonitor.Infrastructure.Auth;

namespace ServerMonitor.Tests.Auth;

public class PasswordServiceTests
{
    private readonly PasswordService _service = new();

    [Fact]
    public void Hash_ProducesDifferentValuesForTheSamePassword()
    {
        // The salt is random per call. Without it identical passwords would produce identical
        // hashes, and the database would show whose password matches whose.
        Assert.NotEqual(_service.Hash("correct horse"), _service.Hash("correct horse"));
    }

    [Fact]
    public void Verify_AcceptsTheRightPassword()
    {
        var hash = _service.Hash("correct horse");

        Assert.Equal(PasswordVerification.Success, _service.Verify(hash, "correct horse"));
    }

    [Fact]
    public void Verify_RejectsTheWrongPassword()
    {
        var hash = _service.Hash("correct horse");

        Assert.Equal(PasswordVerification.Failed, _service.Verify(hash, "correct horse!"));
    }

    [Fact]
    public void Verify_RejectsAPasswordThatOnlyLooksTheSame()
    {
        // The second string ends with a Cyrillic "e". It reads identically and hashes
        // differently, which is the whole point of comparing bytes rather than glyphs.
        var hash = _service.Hash("correct horse");

        Assert.Equal(PasswordVerification.Failed, _service.Verify(hash, "correct horsе"));
    }

    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var hash = _service.Hash("Correct Horse");

        Assert.Equal(PasswordVerification.Failed, _service.Verify(hash, "correct horse"));
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("")]
    [InlineData("!!!")]
    public void Verify_RejectsGarbageWithoutThrowing(string storedHash)
    {
        // The column can hold junk — after a manual edit of the database, say.
        // That is a failed login, not a crash.
        Assert.Equal(PasswordVerification.Failed, _service.Verify(storedHash, "any password"));
    }

    [Fact]
    public void Verify_RejectsEmptyPassword()
    {
        var hash = _service.Hash("correct horse");

        Assert.Equal(PasswordVerification.Failed, _service.Verify(hash, string.Empty));
    }

    [Fact]
    public void Hash_DoesNotContainThePassword()
    {
        // Obvious but cheap: if someone ever swaps the implementation for encoding instead
        // of hashing, this test catches it.
        Assert.DoesNotContain("correct horse", _service.Hash("correct horse"));
    }
}
