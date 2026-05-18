using OpenHab.Core;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Media.SpeechRecognition;

namespace OpenHab.Windows.Tray.Voice;

[ExcludeFromCodeCoverage(Justification = "Windows speech recognition API integration.")]
public sealed class WindowsSpeechVoiceRecognitionService
{
    private const string SpeechUnavailableMessage = "Windows speech recognition is unavailable. Check microphone and online speech settings.";
    private const string MicrophonePermissionMessage = "Microphone permission is required for voice commands.";
    private const string SpeechPrivacyPolicyMessage = "Voice commands require Windows online speech recognition to be enabled in privacy settings.";
    private const string NoMatchMessage = "No voice command recognized.";

    public event EventHandler<VoiceRecognitionActivityEventArgs>? ActivityChanged;

    public async Task<VoiceRecognitionResult> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var recognizer = new SpeechRecognizer();
            recognizer.HypothesisGenerated += (_, args) =>
            {
                ActivityChanged?.Invoke(
                    this,
                    new VoiceRecognitionActivityEventArgs(VoiceRecognitionActivityKind.HypothesisGenerated, args.Hypothesis?.Text));
            };
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                SpeechRecognitionScenario.Dictation,
                "openhab-voice-dictation"));

            var compileResult = await recognizer.CompileConstraintsAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (compileResult.Status != SpeechRecognitionResultStatus.Success)
            {
                return VoiceRecognitionResult.Failed(
                    VoiceRecognitionFailure.PermissionOrSpeechDisabled,
                    SpeechUnavailableMessage);
            }

            ActivityChanged?.Invoke(this, new VoiceRecognitionActivityEventArgs(VoiceRecognitionActivityKind.ListeningStarted, null));
            var recognitionResult = await recognizer.RecognizeAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (recognitionResult is null)
            {
                return VoiceRecognitionResult.Failed(VoiceRecognitionFailure.NoMatch, NoMatchMessage);
            }

            return recognitionResult.Status switch
            {
                SpeechRecognitionResultStatus.Success => ResolveSuccessfulResult(recognitionResult.Text),
                SpeechRecognitionResultStatus.UserCanceled => VoiceRecognitionResult.Failed(
                    VoiceRecognitionFailure.Canceled,
                    "Voice command canceled."),
                SpeechRecognitionResultStatus.AudioQualityFailure => VoiceRecognitionResult.Failed(
                    VoiceRecognitionFailure.NoMatch,
                    NoMatchMessage),
                SpeechRecognitionResultStatus.TimeoutExceeded => VoiceRecognitionResult.Failed(
                    VoiceRecognitionFailure.NoMatch,
                    NoMatchMessage),
                SpeechRecognitionResultStatus.PauseLimitExceeded => VoiceRecognitionResult.Failed(
                    VoiceRecognitionFailure.NoMatch,
                    NoMatchMessage),
                _ => VoiceRecognitionResult.Failed(VoiceRecognitionFailure.NoMatch, NoMatchMessage)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return VoiceRecognitionResult.Failed(
                VoiceRecognitionFailure.PermissionOrSpeechDisabled,
                MicrophonePermissionMessage);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Voice recognition failed: {ex.GetType().Name}: {ex.Message}");
            return ResolveExceptionResult(ex);
        }
    }

    internal static VoiceRecognitionResult ResolveExceptionResultForTesting(Exception exception)
    {
        return ResolveExceptionResult(exception);
    }

    private static VoiceRecognitionResult ResolveExceptionResult(Exception exception)
    {
        if (exception is COMException comException
            && comException.Message.Contains("speech privacy policy", StringComparison.OrdinalIgnoreCase))
        {
            return VoiceRecognitionResult.Failed(
                VoiceRecognitionFailure.PermissionOrSpeechDisabled,
                SpeechPrivacyPolicyMessage);
        }

        return VoiceRecognitionResult.Failed(
            VoiceRecognitionFailure.Failed,
            "Voice recognition failed.");
    }

    private static VoiceRecognitionResult ResolveSuccessfulResult(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        return normalized.Length > 0
            ? VoiceRecognitionResult.Success(normalized)
            : VoiceRecognitionResult.Failed(VoiceRecognitionFailure.NoMatch, NoMatchMessage);
    }
}
