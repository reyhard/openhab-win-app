namespace OpenHab.Windows.Tray.Voice;

public enum VoiceRecognitionActivityKind
{
    ListeningStarted,
    HypothesisGenerated
}

public sealed class VoiceRecognitionActivityEventArgs : EventArgs
{
    public VoiceRecognitionActivityEventArgs(VoiceRecognitionActivityKind kind, string? text)
    {
        Kind = kind;
        Text = text;
    }

    public VoiceRecognitionActivityKind Kind { get; }

    public string? Text { get; }
}
