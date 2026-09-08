using ServerMonitor.Infrastructure.Auth;

namespace ServerMonitor.Tests.Auth;

public class PasswordServiceTests
{
    private readonly PasswordService _service = new();

    [Fact]
    public void Hash_ProducesDifferentValuesForTheSamePassword()
    {
        // Соль случайна на каждый вызов. Без неё одинаковые пароли давали бы одинаковые
        // хеши, и по базе было бы видно, у кого пароль совпадает с чужим.
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
        // В колонке может оказаться мусор — например, после ручной правки базы.
        // Это отказ во входе, а не падение приложения.
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
        // Очевидное, но дешёвое: если однажды кто-то заменит реализацию на кодирование
        // вместо хеширования, тест это поймает.
        Assert.DoesNotContain("correct horse", _service.Hash("correct horse"));
    }
}
