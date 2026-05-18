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
}
