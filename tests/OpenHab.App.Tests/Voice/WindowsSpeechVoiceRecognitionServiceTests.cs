using OpenHab.Windows.Tray.Voice;
using System.Runtime.InteropServices;

namespace OpenHab.App.Tests.Voice;

public sealed class WindowsSpeechVoiceRecognitionServiceTests
{
    [Fact]
    public void ResolveExceptionResultMapsSpeechPrivacyPolicyFailureToActionableMessage()
    {
        var result = WindowsSpeechVoiceRecognitionService.ResolveExceptionResultForTesting(
            new COMException("The speech privacy policy was not accepted prior to attempting a speech recognition."));

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceRecognitionFailure.PermissionOrSpeechDisabled, result.Failure);
        Assert.Contains("online speech", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveSettingsUriMapsPermissionFailuresToRelevantWindowsSettings()
    {
        var speechPrivacyResult = VoiceRecognitionResult.Failed(
            VoiceRecognitionFailure.PermissionOrSpeechDisabled,
            "Voice commands require Windows online speech recognition to be enabled in privacy settings.");
        var microphoneResult = VoiceRecognitionResult.Failed(
            VoiceRecognitionFailure.PermissionOrSpeechDisabled,
            "Microphone permission is required for voice commands.");
        var genericResult = VoiceRecognitionResult.Failed(
            VoiceRecognitionFailure.Failed,
            "Voice recognition failed.");

        Assert.Equal(new Uri("ms-settings:privacy-speech"), VoiceRecognitionSettingsUriResolver.Resolve(speechPrivacyResult));
        Assert.Equal(new Uri("ms-settings:privacy-microphone"), VoiceRecognitionSettingsUriResolver.Resolve(microphoneResult));
        Assert.Null(VoiceRecognitionSettingsUriResolver.Resolve(genericResult));
    }
}
