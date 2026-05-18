using OpenHab.App.Runtime;
using OpenHab.App.Shortcuts;
using OpenHab.Core.Api;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class VoiceCommandExecutorTests
{
    [Fact]
    public async Task SendsRecognizedPhraseToVoiceTargetItem()
    {
        var client = new RecordingVoiceClient();
        var executor = CreateExecutor(() => client);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, "  turn on the kitchen lights  ", logPhrase: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", "turn on the kitchen lights"), Assert.Single(client.Commands));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task EmptyPhraseDoesNotSend(string recognizedPhrase)
    {
        var client = new RecordingVoiceClient();
        var executor = CreateExecutor(() => client);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, recognizedPhrase, logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.EmptyPhrase, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task DisconnectedDoesNotCreateClient()
    {
        var clientCreated = false;
        var executor = CreateExecutor(() =>
        {
            clientCreated = true;
            return new RecordingVoiceClient();
        }, () => ConnectionState.Offline);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, "turn on", logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.Disconnected, result.Failure);
        Assert.False(clientCreated);
    }

    [Fact]
    public async Task InvalidNonVoiceActionDoesNotSend()
    {
        var clientCreated = false;
        var executor = CreateExecutor(() =>
        {
            clientCreated = true;
            return new RecordingVoiceClient();
        });
        var action = new ShortcutAction(
            "a1",
            "Action",
            "play",
            true,
            null,
            "LivingRoom_Item",
            ShortcutCommandType.SendCommand,
            "PLAY");

        var result = await executor.ExecuteAsync(action, "turn on", logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.InvalidAction, result.Failure);
        Assert.False(clientCreated);
    }

    [Fact]
    public async Task NormalDiagnosticsDoNotLogPhrase()
    {
        var diagnostics = new List<string>();
        var executor = CreateExecutor(() => new RecordingVoiceClient(), logDiagnostic: diagnostics.Add);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, "turn on", logPhrase: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(diagnostics);
        Assert.Equal("Sending voice command phrase to item 'LivingRoom_Item'.", diagnostics[0]);
        Assert.DoesNotContain("turn on", diagnostics[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerboseDiagnosticsCanLogPhrase()
    {
        var diagnostics = new List<string>();
        var executor = CreateExecutor(() => new RecordingVoiceClient(), logDiagnostic: diagnostics.Add);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, "turn on", logPhrase: true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(diagnostics);
        Assert.Equal("Sending voice command phrase to item 'LivingRoom_Item': turn on", diagnostics[0]);
    }

    [Fact]
    public async Task CommandExceptionReturnsCommandFailed()
    {
        var client = new RecordingVoiceClient
        {
            SendCommandException = new InvalidOperationException("boom")
        };
        var executor = CreateExecutor(() => client);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, "turn on", logPhrase: true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.CommandFailed, result.Failure);
        Assert.Equal("Voice command could not be sent.", result.Message);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task NullClientReturnsMissingClientWithoutThrowing()
    {
        var executor = CreateExecutor(() => null);
        var action = VoiceAction();

        var result = await executor.ExecuteAsync(action, "turn on", logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.MissingClient, result.Failure);
        Assert.Equal("Client is unavailable.", result.Message);
    }

    [Fact]
    public async Task SendCommandCancellationIsRethrown()
    {
        var client = new RecordingVoiceClient
        {
            SendCommandException = new OperationCanceledException("cancel")
        };
        var executor = CreateExecutor(() => client);
        var action = VoiceAction();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(action, "turn on", logPhrase: false, CancellationToken.None));
    }

    private static VoiceCommandExecutor CreateExecutor(
        Func<IOpenHabClient?> getClient,
        Func<ConnectionState>? getConnectionState = null,
        Action<string>? logDiagnostic = null)
    {
        return new VoiceCommandExecutor(
            getClient,
            getConnectionState ?? (() => ConnectionState.Online),
            logDiagnostic ?? (_ => { }));
    }

    private static ShortcutAction VoiceAction()
    {
        return new ShortcutAction(
            "built-in.voice.default",
            "Voice command",
            "microphone",
            true,
            null,
            "LivingRoom_Item",
            ShortcutCommandType.Voice,
            null);
    }

    private sealed class RecordingVoiceClient : IOpenHabClient
    {
        public List<(string ItemName, string Command)> Commands { get; } = new();
        public Exception? SendCommandException { get; set; }

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
        public Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MainUiPageComponent>>([]);
    }
}
