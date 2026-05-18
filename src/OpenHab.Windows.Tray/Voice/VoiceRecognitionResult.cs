namespace OpenHab.Windows.Tray.Voice;

public enum VoiceRecognitionFailure
{
    None,
    NoMatch,
    Canceled,
    PermissionOrSpeechDisabled,
    Unavailable,
    Failed
}

public sealed record VoiceRecognitionResult(
    bool Succeeded,
    string? Text,
    VoiceRecognitionFailure Failure,
    string Message)
{
    public static VoiceRecognitionResult Success(string text)
    {
        return new VoiceRecognitionResult(true, text, VoiceRecognitionFailure.None, string.Empty);
    }

    public static VoiceRecognitionResult Failed(VoiceRecognitionFailure failure, string message)
    {
        return new VoiceRecognitionResult(false, null, failure, message);
    }
}
