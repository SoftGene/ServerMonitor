using ServerMonitor.Infrastructure.Agents;

namespace ServerMonitor.Tests.Agents;

public class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ProducesDifferentKeysEveryTime()
    {
        var first = ApiKeyGenerator.Generate();
        var second = ApiKeyGenerator.Generate();

        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Hash_IsStableForTheSameKey()
    {
        var generated = ApiKeyGenerator.Generate();

        Assert.Equal(generated.Hash, ApiKeyGenerator.Hash(generated.Key));
    }

    [Fact]
    public void Hash_DoesNotContainTheKeyItself()
    {
        // В базе лежит только хеш: утечка дампа не должна отдавать рабочие ключи.
        var generated = ApiKeyGenerator.Generate();

        Assert.DoesNotContain(generated.Key, generated.Hash);
    }

    [Fact]
    public void GeneratedKey_IsUrlSafeAndLongEnough()
    {
        var generated = ApiKeyGenerator.Generate();

        // 32 случайных байта в base64url — перебирать нечего, поэтому медленный хеш не нужен.
        Assert.True(generated.Key.Length >= 40, $"Ключ слишком короткий: {generated.Key.Length}.");
        Assert.DoesNotContain('+', generated.Key);
        Assert.DoesNotContain('/', generated.Key);
        Assert.DoesNotContain('=', generated.Key);
    }

    [Fact]
    public void FixedTimeEquals_ComparesValues()
    {
        Assert.True(ApiKeyGenerator.FixedTimeEquals("abc", "abc"));
        Assert.False(ApiKeyGenerator.FixedTimeEquals("abc", "abd"));
        Assert.False(ApiKeyGenerator.FixedTimeEquals("abc", "abcd"));
        Assert.False(ApiKeyGenerator.FixedTimeEquals("", "abc"));
    }
}
