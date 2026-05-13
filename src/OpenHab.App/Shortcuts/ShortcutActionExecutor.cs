using OpenHab.App.Runtime;
using OpenHab.Core.Api;

namespace OpenHab.App.Shortcuts;

public enum ShortcutActionExecutionFailure
{
    None,
    Disconnected,
    InvalidAction,
    MissingClient,
    UnsupportedState,
    CommandFailed,
    SurfaceRequired
}

public sealed record ShortcutActionExecutionResult(
    bool Succeeded,
    ShortcutActionExecutionFailure Failure,
    string Message)
{
    public static ShortcutActionExecutionResult Success(string message = "")
    {
        return new ShortcutActionExecutionResult(true, ShortcutActionExecutionFailure.None, message);
    }

    public static ShortcutActionExecutionResult Failed(ShortcutActionExecutionFailure failure, string message)
    {
        return new ShortcutActionExecutionResult(false, failure, message);
    }
}

public sealed class ShortcutActionExecutor
{
    private readonly Func<IOpenHabClient?> getClient;
    private readonly Func<ConnectionState> getConnectionState;

    public ShortcutActionExecutor(Func<IOpenHabClient?> getClient, Func<ConnectionState> getConnectionState)
    {
        this.getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
        this.getConnectionState = getConnectionState ?? throw new ArgumentNullException(nameof(getConnectionState));
    }

    public async Task<ShortcutActionExecutionResult> ExecuteAsync(
        ShortcutAction action,
        CancellationToken cancellationToken = default)
    {
        var validation = ShortcutValidation.ValidateAction(action);
        if (!validation.IsValid)
        {
            var message = string.Join("; ", validation.Errors);
            return ShortcutActionExecutionResult.Failed(ShortcutActionExecutionFailure.InvalidAction, message);
        }

        if (getConnectionState() != ConnectionState.Online)
        {
            return ShortcutActionExecutionResult.Failed(
                ShortcutActionExecutionFailure.Disconnected,
                "Cannot execute action while disconnected.");
        }

        try
        {
            var client = getClient();
            if (client is null)
            {
                return ShortcutActionExecutionResult.Failed(
                    ShortcutActionExecutionFailure.MissingClient,
                    "Client is unavailable.");
            }

            var commandResolution = await ResolveCommandAsync(client, action, cancellationToken).ConfigureAwait(false);
            if (!commandResolution.Succeeded)
            {
                return commandResolution.FailureResult;
            }

            await client.SendCommandAsync(action.TargetItem, commandResolution.Command!, cancellationToken)
                .ConfigureAwait(false);
            return ShortcutActionExecutionResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ShortcutActionExecutionResult.Failed(
                ShortcutActionExecutionFailure.CommandFailed,
                ex.Message);
        }
    }

    private static async Task<CommandResolution> ResolveCommandAsync(
        IOpenHabClient client,
        ShortcutAction action,
        CancellationToken cancellationToken)
    {
        switch (action.CommandType)
        {
            case ShortcutCommandType.Toggle:
            {
                var state = (await client.GetItemStateAsync(action.TargetItem, cancellationToken).ConfigureAwait(false) ?? string.Empty)
                    .Trim();
                if (state.Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResolution.FromCommand("OFF");
                }

                if (state.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResolution.FromCommand("ON");
                }

                return CommandResolution.FromFailure(
                    ShortcutActionExecutionResult.Failed(
                        ShortcutActionExecutionFailure.UnsupportedState,
                        $"Unsupported toggle state '{state}'."));
            }
            case ShortcutCommandType.OnOff:
            {
                var normalized = (action.CommandValue ?? string.Empty).Trim();
                if (normalized.Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResolution.FromCommand("ON");
                }

                return CommandResolution.FromCommand("OFF");
            }
            case ShortcutCommandType.OpenClose:
            {
                var normalized = (action.CommandValue ?? string.Empty).Trim();
                if (normalized.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResolution.FromCommand("OPEN");
                }

                return CommandResolution.FromCommand("CLOSE");
            }
            case ShortcutCommandType.SendCommand:
                return CommandResolution.FromCommand((action.CommandValue ?? string.Empty).Trim());
            case ShortcutCommandType.OpenSlider:
            case ShortcutCommandType.OpenColorPicker:
                return CommandResolution.FromFailure(
                    ShortcutActionExecutionResult.Failed(
                        ShortcutActionExecutionFailure.SurfaceRequired,
                        "This action requires opening an interactive surface."));
            default:
                return CommandResolution.FromFailure(
                    ShortcutActionExecutionResult.Failed(
                        ShortcutActionExecutionFailure.InvalidAction,
                        "Command type is invalid."));
        }
    }

    private sealed record CommandResolution(bool Succeeded, string? Command, ShortcutActionExecutionResult FailureResult)
    {
        public static CommandResolution FromCommand(string command)
        {
            return new CommandResolution(true, command, ShortcutActionExecutionResult.Success());
        }

        public static CommandResolution FromFailure(ShortcutActionExecutionResult failureResult)
        {
            return new CommandResolution(false, null, failureResult);
        }
    }
}
