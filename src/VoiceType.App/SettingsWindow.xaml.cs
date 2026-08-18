using System.Windows;
using VoiceType.Core.Audio;
using VoiceType.Core.Security;
using VoiceType.Core.Storage;

namespace VoiceType.App;

public partial class SettingsWindow : Window
{
    private sealed record MicChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ISecretStore _secrets;
    private readonly ISettingsStore _settings;

    public SettingsWindow(ISecretStore secrets, ISettingsStore settings, IAudioCapture audio)
    {
        InitializeComponent();
        _secrets = secrets;
        _settings = settings;

        var current = _settings.Load();
        MicCombo.Items.Add(new MicChoice(null, "System default"));
        try
        {
            foreach (var device in audio.EnumerateDevices())
                MicCombo.Items.Add(new MicChoice(device.Id, device.IsDefault ? $"{device.Name} (default)" : device.Name));
        }
        catch
        {
            // no devices — the default entry stays
        }

        MicCombo.SelectedIndex = 0;
        for (int i = 1; i < MicCombo.Items.Count; i++)
        {
            if (((MicChoice)MicCombo.Items[i]).Id == current.MicrophoneDeviceId)
            {
                MicCombo.SelectedIndex = i;
                break;
            }
        }

        foreach (string term in current.DictionaryTerms)
            TermsList.Items.Add(term);

        UpdateKeyStatus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var choice = (MicChoice)MicCombo.SelectedItem;
        _settings.Save(_settings.Load() with
        {
            MicrophoneDeviceId = choice.Id,
            DictionaryTerms = TermsList.Items.Cast<string>().ToArray(),
        });

        string key = KeyBox.Password.Trim();
        if (key.Length > 0)
        {
            _secrets.SetApiKey(key);
            KeyBox.Clear();
        }

        UpdateKeyStatus();
        KeyStatus.Text += "  Saved.";
    }

    private void OnRemoveKey(object sender, RoutedEventArgs e)
    {
        _secrets.Clear();
        UpdateKeyStatus();
    }

    private void OnAddTerm(object sender, RoutedEventArgs e)
    {
        string term = NewTermBox.Text.Trim();
        if (term.Length == 0 || TermsList.Items.Cast<string>().Any(t => t.Equals(term, StringComparison.OrdinalIgnoreCase)))
            return;
        TermsList.Items.Add(term);
        NewTermBox.Clear();
        PersistTerms();
    }

    private void OnRemoveTerm(object sender, RoutedEventArgs e)
    {
        if (TermsList.SelectedItem is not string term) return;
        TermsList.Items.Remove(term);
        PersistTerms();
    }

    /// <summary>
    /// Add/Remove persist immediately: an unsaved in-memory list meant cleanup
    /// ran with an empty dictionary until Save was clicked.
    /// </summary>
    private void PersistTerms()
    {
        _settings.Save(_settings.Load() with
        {
            DictionaryTerms = TermsList.Items.Cast<string>().ToArray(),
        });
        DictionaryStatus.Text = "Saved ✓";
    }

    private void OnTermSelectionChanged(object sender, RoutedEventArgs e) =>
        RemoveTermButton.IsEnabled = TermsList.SelectedItem is not null;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateKeyStatus()
    {
        bool has = _secrets.HasApiKey;
        KeyStatus.Text = has ? "Key stored ✓" : "No key stored";
        RemoveKeyButton.IsEnabled = has;
    }
}
