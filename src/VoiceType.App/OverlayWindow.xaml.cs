using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VoiceType.Core.Dictation;

namespace VoiceType.App;

/// <summary>
/// Status pill at the bottom of the primary screen. WS_EX_NOACTIVATE plus
/// ShowActivated=false: it never takes focus, so the caret stays in the
/// target app.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private static readonly System.Windows.Media.Brush RecordingBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD9, 0x30, 0x25));
    private static readonly System.Windows.Media.Brush WorkingBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x73, 0xE8));
    private static readonly System.Windows.Media.Brush ErrorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0xAB, 0x00));

    private readonly DispatcherTimer _errorHide;

    public OverlayWindow()
    {
        InitializeComponent();
        _errorHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _errorHide.Tick += (_, _) => { _errorHide.Stop(); Hide(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint handle = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    public void ShowState(DictationState state)
    {
        _errorHide.Stop();
        switch (state)
        {
            case DictationState.Recording:
                StatusDot.Fill = RecordingBrush;
                StatusText.Text = "Recording — release to paste, Esc to cancel";
                PartialText.Text = string.Empty;
                SetLevel(0f);
                ShowPill();
                break;
            case DictationState.Finalizing:
                StatusDot.Fill = WorkingBrush;
                StatusText.Text = "Transcribing…";
                SetLevel(0f);
                ShowPill();
                break;
            case DictationState.Cleaning:
                StatusDot.Fill = WorkingBrush;
                StatusText.Text = "Cleaning up…";
                ShowPill();
                break;
            case DictationState.Inserting:
                StatusDot.Fill = WorkingBrush;
                StatusText.Text = "Inserting…";
                ShowPill();
                break;
            default:
                Hide();
                break;
        }
    }

    public void ShowPartial(string text)
    {
        if (IsVisible) PartialText.Text = text;
    }

    public void SetLevel(float level)
    {
        LevelBar.Width = 80 * Math.Clamp(level, 0f, 1f);
    }

    public void ShowError(string message)
    {
        StatusDot.Fill = ErrorBrush;
        StatusText.Text = message;
        PartialText.Text = string.Empty;
        SetLevel(0f);
        ShowPill();
        _errorHide.Stop();
        _errorHide.Start();
    }

    private void ShowPill()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 48;
        if (!IsVisible) Show();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}
