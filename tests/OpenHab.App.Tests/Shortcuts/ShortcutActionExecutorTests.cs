using OpenHab.App.Runtime;
using OpenHab.App.Shortcuts;
using OpenHab.Core.Api;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutActionExecutorTests
{
    [Fact]
    public async Task BlocksExecutionWhenDisconnected()
    {
        var clientInvoked = false;
        var executor = new ShortcutActionExecutor(
            () =>
            {
                clientInvoked = true;
                throw new InvalidOperationException("getClient must not be called while disconnected.");
            },
            () => ConnectionState.Offline);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.Disconnected, result.Failure);
        Assert.False(clientInvoked);
    }

    [Fact]
    public async Task SendCommandSendsConfiguredValue()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", "PLAY"), Assert.Single(client.Commands));
    }

    [Theory]
    [InlineData("ON", "OFF")]
    [InlineData("OFF", "ON")]
    public async Task ToggleReadsStateAndSendsOpposite(string current, string expected)
    {
        var client = new RecordingShortcutClient();
        client.ItemStates["LivingRoom_Item"] = current;
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.Toggle, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", expected), Assert.Single(client.Commands));
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("PLAY")]
    public async Task ToggleFailsForUnknownState(string state)
    {
        var client = new RecordingShortcutClient();
        client.ItemStates["LivingRoom_Item"] = state;
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.Toggle, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.UnsupportedState, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task ToggleFailsForMissingState()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.Toggle, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.UnsupportedState, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Theory]
    [InlineData(ShortcutCommandType.OnOff, "ON")]
    [InlineData(ShortcutCommandType.OnOff, "OFF")]
    [InlineData(ShortcutCommandType.OpenClose, "OPEN")]
    [InlineData(ShortcutCommandType.OpenClose, "CLOSE")]
    public async Task DiscreteCommandsSendConfiguredValue(ShortcutCommandType type, string value)
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(type, value);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", value), Assert.Single(client.Commands));
    }

    [Fact]
    public async Task OnOffNormalizesWhitespaceAndCase()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.OnOff, " on ");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", "ON"), Assert.Single(client.Commands));
    }

    [Fact]
    public async Task OpenCloseNormalizesWhitespaceAndCase()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.OpenClose, " close ");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", "CLOSE"), Assert.Single(client.Commands));
    }

    [Fact]
    public async Task SendCommandTrimsWhitespace()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, " PLAY ");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", "PLAY"), Assert.Single(client.Commands));
    }

    [Theory]
    [InlineData(ShortcutCommandType.OpenSlider)]
    [InlineData(ShortcutCommandType.OpenColorPicker)]
    public async Task InteractiveSurfacesReturnSurfaceRequired(ShortcutCommandType type)
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(type, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.SurfaceRequired, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task VoiceActionReturnsSurfaceRequiredAndDoesNotSendCommand()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = new ShortcutAction(
            "built-in.voice.default",
            "Voice command",
            "microphone",
            true,
            null,
            "VoiceCommand",
            ShortcutCommandType.Voice,
            null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.SurfaceRequired, result.Failure);
        Assert.Equal("Voice actions require voice command activation.", result.Message);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task InvalidActionReturnsInvalidAction()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.InvalidAction, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task MissingClientReturnsMissingClient()
    {
        var executor = new ShortcutActionExecutor(() => null, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.MissingClient, result.Failure);
    }

    [Fact]
    public async Task ClientProviderExceptionReturnsCommandFailed()
    {
        var providerInvoked = false;
        var executor = new ShortcutActionExecutor(
            () =>
            {
                providerInvoked = true;
                throw new InvalidOperationException("provider failed");
            },
            () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(providerInvoked);
        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.CommandFailed, result.Failure);
        Assert.Equal("Command could not be sent.", result.Message);
    }

    [Fact]
    public async Task ConnectionStateProviderExceptionReturnsCommandFailedWithoutInvokingClient()
    {
        var clientInvoked = false;
        var executor = new ShortcutActionExecutor(
            () =>
            {
                clientInvoked = true;
                return new RecordingShortcutClient();
            },
            () => throw new InvalidOperationException("state provider failed"));
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(clientInvoked);
        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.CommandFailed, result.Failure);
        Assert.Equal("Command could not be sent.", result.Message);
    }

    [Fact]
    public async Task ClientProviderCancellationIsRethrown()
    {
        var executor = new ShortcutActionExecutor(
            () => throw new OperationCanceledException("cancel"),
            () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(action, CancellationToken.None));
    }

    [Fact]
    public async Task ConnectionStateProviderCancellationIsRethrown()
    {
        var executor = new ShortcutActionExecutor(
            () => new RecordingShortcutClient(),
            () => throw new OperationCanceledException("cancel"));
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(action, CancellationToken.None));
    }

    [Fact]
    public async Task CommandExceptionReturnsCommandFailed()
    {
        var client = new RecordingShortcutClient
        {
            SendCommandException = new InvalidOperationException("boom token=secret-123")
        };
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.CommandFailed, result.Failure);
        Assert.Equal("Command could not be sent.", result.Message);
        Assert.DoesNotContain("secret-123", result.Message, StringComparison.Ordinal);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task SendCommandCancellationIsRethrown()
    {
        var client = new RecordingShortcutClient
        {
            SendCommandException = new OperationCanceledException("cancel")
        };
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(action, CancellationToken.None));
    }

    [Fact]
    public async Task ToggleStateLookupCancellationIsRethrown()
    {
        var client = new RecordingShortcutClient
        {
            GetItemStateException = new OperationCanceledException("cancel")
        };
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.Toggle, null);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(action, CancellationToken.None));
    }

    private static ShortcutAction Action(ShortcutCommandType type, string? value)
    {
        return new ShortcutAction("a1", "Action", "play", true, null, "LivingRoom_Item", type, value);
    }

    private sealed class RecordingShortcutClient : IOpenHabClient
    {
        public List<(string ItemName, string Command)> Commands { get; } = new();
        public Dictionary<string, string?> ItemStates { get; } = new(StringComparer.Ordinal);
        public Exception? SendCommandException { get; set; }
        public Exception? GetItemStateException { get; set; }

        public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
        {
            if (SendCommandException is not null)
            {
                throw SendCommandException;
            }

            Commands.Add((itemName, command));
            return Task.CompletedTask;
        }

        public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SitemapInfo>>([]);
        public Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OpenHabItemSummary>>([]);
        public Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MainUiPageComponent>>([]);

        public Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken)
        {
            if (GetItemStateException is not null)
            {
                throw GetItemStateException;
            }

            ItemStates.TryGetValue(itemName, out var state);
            return Task.FromResult(state);
        }
    }
}
