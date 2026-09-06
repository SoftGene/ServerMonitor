using ServerMonitor.Agent;

namespace ServerMonitor.Tests.AgentTests;

public class AgentStateTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"agent-state-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task LoadAsync_ReturnsNullWhenFileIsMissing()
    {
        // Первый запуск: файла ещё нет, и это не ошибка — это сигнал «пора регистрироваться».
        Assert.Null(await AgentState.LoadAsync(TempPath(), CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsTheState()
    {
        var path = TempPath();
        var state = new AgentState { ServerId = Guid.NewGuid(), ApiKey = "secret-key" };

        try
        {
            await state.SaveAsync(path, CancellationToken.None);

            var loaded = await AgentState.LoadAsync(path, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(state.ServerId, loaded.ServerId);
            Assert.Equal(state.ApiKey, loaded.ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousState()
    {
        var path = TempPath();

        try
        {
            await new AgentState { ServerId = Guid.NewGuid(), ApiKey = "first" }
                .SaveAsync(path, CancellationToken.None);

            var second = new AgentState { ServerId = Guid.NewGuid(), ApiKey = "second" };
            await second.SaveAsync(path, CancellationToken.None);

            var loaded = await AgentState.LoadAsync(path, CancellationToken.None);

            Assert.Equal("second", loaded!.ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
