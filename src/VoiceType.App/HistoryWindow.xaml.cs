using System.Windows;
using VoiceType.Core.Storage;

namespace VoiceType.App;

/// <summary>Newest-first transcript history with Copy and Delete. Text only — no audio exists.</summary>
public partial class HistoryWindow : Window
{
    private sealed record Row(HistoryEntry Entry)
    {
        public override string ToString()
        {
            string time = Entry.TimestampUtc.ToLocalTime().ToString("MMM d, h:mm tt");
            string latency = Entry.LatencyMs is { } ms ? $" · {ms / 1000.0:0.0}s" : string.Empty;
            string preview = Entry.Text.Length > 70 ? Entry.Text[..70] + "…" : Entry.Text;
            return $"{time} · {Entry.DeliveryStatus}{latency} — {preview.ReplaceLineEndings(" ")}";
        }
    }

    private readonly IHistoryStore _history;
    private readonly Func<string, bool> _copyToClipboard;

    public HistoryWindow(IHistoryStore history, Func<string, bool> copyToClipboard)
    {
        InitializeComponent();
        _history = history;
        _copyToClipboard = copyToClipboard;
        Reload();
    }

    private void Reload()
    {
        EntryList.Items.Clear();
        foreach (var entry in _history.GetAll())
            EntryList.Items.Add(new Row(entry));
        FullText.Text = string.Empty;
        CopyButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        var row = EntryList.SelectedItem as Row;
        FullText.Text = row?.Entry.Text ?? string.Empty;
        CopyButton.IsEnabled = row is not null;
        DeleteButton.IsEnabled = row is not null;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not Row row) return;
        CopyButton.Content = _copyToClipboard(row.Entry.Text) ? "Copied ✓" : "Copy failed";
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not Row row) return;
        _history.Delete(row.Entry.Id);
        Reload();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
