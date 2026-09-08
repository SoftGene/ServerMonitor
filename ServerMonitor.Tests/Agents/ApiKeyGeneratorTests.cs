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
        // Only the hash is stored: a leaked dump must not hand out working keys.
        var generated = ApiKeyGenerator.Generate();

        Assert.DoesNotContain(generated.Key, generated.Hash);
    }

    [Fact]
    public void GeneratedKey_IsUrlSafeAndLongEnough()
    {
        var generated = ApiKeyGenerator.Generate();

        // 32 random bytes in base64url — nothing to enumerate, so no slow hash is needed.
        Assert.True(generated.Key.Length >= 40, $"The key is too short: {generated.Key.Length}.");
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
