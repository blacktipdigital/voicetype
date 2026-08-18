using System.Windows;
using VoiceType.Core.Dictation;

namespace VoiceType.App;

/// <summary>
/// Recovery popup: shown when insertion is refused or fails. The transcript
/// is already on the clipboard (when possible); this window keeps it
/// recoverable and editable.
/// </summary>
public partial class RecoveryWindow : Window
{
    private readonly Func<string, bool> _copyToClipboard;
    private readonly Action _openHistory;

    public RecoveryWindow(RecoveryContext context, Func<string, bool> copyToClipboard, Action openHistory)
    {
        InitializeComponent();
        _copyToClipboard = copyToClipboard;
        _openHistory = openHistory;

        ReasonText.Text = DescribeReason(context.Reason);
        ClipboardStatusText.Text = context.CopiedToClipboard
            ? "The text is on your clipboard — paste it where you need it."
            : "Clipboard was unavailable. Use Copy Again or copy the text below manually.";
        TranscriptBox.Text = context.Transcript;
    }

    private void OnCopyAgain(object sender, RoutedEventArgs e)
    {
        bool copied = _copyToClipboard(TranscriptBox.Text);
        CopyAgainButton.Content = copied ? "Copied ✓" : "Copy failed";
        ClipboardStatusText.Text = copied
            ? "Copied to clipboard."
            : "Clipboard is still unavailable — copy the text manually.";
    }

    private void OnOpenHistory(object sender, RoutedEventArgs e) => _openHistory();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static string DescribeReason(RecoveryReason reason) => reason switch
    {
        RecoveryReason.NoTarget => "No insertion target was captured.",
        RecoveryReason.PasswordField => "The focused field is a password field — VoiceType never pastes there.",
        RecoveryReason.FocusChanged => "The focused window changed during dictation.",
        RecoveryReason.HigherIntegrityTarget => "The target app runs elevated; Windows blocks input from VoiceType.",
        RecoveryReason.UneditableTarget => "The focused control doesn't look editable.",
        RecoveryReason.InjectionFailed => "The paste keystroke could not be delivered.",
        _ => "Insertion did not complete.",
    };
}
