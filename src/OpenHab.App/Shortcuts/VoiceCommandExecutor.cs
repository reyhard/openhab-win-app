using OpenHab.App.Runtime;
using OpenHab.Core.Api;

namespace OpenHab.App.Shortcuts;

public enum VoiceCommandExecutionFailure
{
    None,
    Disconnected,
    InvalidAction,
    EmptyPhrase,
    MissingClient,
    CommandFailed
}

public sealed record VoiceCommandExecutionResult(
    bool Succeeded,
    VoiceCommandExecutionFailure Failure,
    string Message)
{
    public static VoiceCommandExecutionResult Success(string message = "")
    {
        return new VoiceCommandExecutionResult(true, VoiceCommandExecutionFailure.None, message);
    }

    public static VoiceCommandExecutionResult Failed(VoiceCommandExecutionFailure failure, string message)
    {
        return new VoiceCommandExecutionResult(false, failure, message);
    }
}

public sealed class VoiceCommandExecutor
{
    private const string CommandFailedMessage = "Voice command could not be sent.";

    private readonly Func<IOpenHabClient?> getClient;
    private readonly Func<ConnectionState> getConnectionState;
    private readonly Action<string> logDiagnostic;

    public VoiceCommandExecutor(
        Func<IOpenHabClient?> getClient,
        Func<ConnectionState> getConnectionState,
        Action<string> logDiagnostic)
    {
        this.getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
        this.getConnectionState = getConnectionState ?? throw new ArgumentNullException(nameof(getConnectionState));
        this.logDiagnostic = logDiagnostic ?? throw new ArgumentNullException(nameof(logDiagnostic));
    }

    public async Task<VoiceCommandExecutionResult> ExecuteAsync(
        ShortcutAction action,
        string recognizedPhrase,
        bool logPhrase,
        CancellationToken cancellationToken)
    {
        if (action is null || action.CommandType != ShortcutCommandType.Voice || !ShortcutValidation.ValidateAction(action).IsValid)
        {
            return VoiceCommandExecutionResult.Failed(
                VoiceCommandExecutionFailure.InvalidAction,
                "Voice action is invalid.");
        }

        var phrase = (recognizedPhrase ?? string.Empty).Trim();
        if (phrase.Length == 0)
        {
            return VoiceCommandExecutionResult.Failed(
                VoiceCommandExecutionFailure.EmptyPhrase,
                "Voice command was empty.");
        }

        try
        {
            if (getConnectionState() != ConnectionState.Online)
            {
                return VoiceCommandExecutionResult.Failed(
                    VoiceCommandExecutionFailure.Disconnected,
                    "Cannot execute voice command while disconnected.");
            }

            var client = getClient();
            if (client is null)
            {
                return VoiceCommandExecutionResult.Failed(
                    VoiceCommandExecutionFailure.MissingClient,
                    "Client is unavailable.");
            }

            logDiagnostic(logPhrase
                ? $"Sending voice command phrase to item '{action.TargetItem}': {phrase}"
                : $"Sending voice command phrase to item '{action.TargetItem}'.");

            await client.SendCommandAsync(action.TargetItem, phrase, cancellationToken).ConfigureAwait(false);
            return VoiceCommandExecutionResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return VoiceCommandExecutionResult.Failed(VoiceCommandExecutionFailure.CommandFailed, CommandFailedMessage);
        }
    }
}
