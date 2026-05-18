namespace OpenHab.Windows.Tray.Voice;

internal static class VoiceRecognitionSettingsUriResolver
{
    private static readonly Uri SpeechPrivacySettingsUri = new("ms-settings:privacy-speech");
    private static readonly Uri MicrophonePrivacySettingsUri = new("ms-settings:privacy-microphone");

    public static Uri? Resolve(VoiceRecognitionResult result)
    {
        if (result.Failure != VoiceRecognitionFailure.PermissionOrSpeechDisabled)
        {
            return null;
        }

        if (result.Message.Contains("microphone", StringComparison.OrdinalIgnoreCase))
        {
            return MicrophonePrivacySettingsUri;
        }

        if (result.Message.Contains("speech", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("online speech", StringComparison.OrdinalIgnoreCase))
        {
            return SpeechPrivacySettingsUri;
        }

        return null;
    }
}
