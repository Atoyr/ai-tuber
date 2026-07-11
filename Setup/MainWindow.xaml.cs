using System.Windows;
using System.Windows.Controls;
using Medoz.Setup.Services;
using Medoz.Voicevox;

namespace Medoz.Setup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadFromEnvironment();
    }

    private void LoadFromEnvironment()
    {
        var s = SetupStore.LoadCurrent();

        // LLM
        SelectProvider(s.LlmProvider);
        AnthropicKeyBox.Password = s.AnthropicApiKey;
        ClaudeModelBox.Text = s.ClaudeModel;
        GeminiKeyBox.Password = s.GeminiApiKey;
        GeminiModelBox.Text = s.GeminiModel;
        OpenAIKeyBox.Password = s.OpenAIApiKey;
        OpenAIModelBox.Text = s.OpenAIModel;

        // VOICEVOX
        VoicevoxUrlBox.Text = s.VoicevoxUrl;
        SpeakerIdBox.Text = s.SpeakerId.ToString();
        PopulateDevices(s.OutputDeviceName);

        // X
        XConsumerKeyBox.Password = s.XConsumerKey;
        XConsumerSecretBox.Password = s.XConsumerSecret;
        XAccessTokenBox.Password = s.XAccessToken;
        XAccessTokenSecretBox.Password = s.XAccessTokenSecret;

        StatusText.Text = "現在の設定を読み込みました。必要な項目を編集して [保存する] を押してください。";
    }

    private void SelectProvider(string provider)
    {
        foreach (var item in ProviderCombo.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
            {
                ProviderCombo.SelectedItem = cbi;
                return;
            }
        }
        ProviderCombo.SelectedIndex = 0;
    }

    private string GetSelectedProvider()
        => (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "claude";

    private void PopulateDevices(string preferred)
    {
        DeviceCombo.Items.Clear();
        string? selected = null;
        try
        {
            var names = AudioPlayer.GetOutputDeviceNames();
            foreach (var n in names)
            {
                DeviceCombo.Items.Add(n);
                if (selected is null && !string.IsNullOrEmpty(preferred)
                    && n.Contains(preferred, StringComparison.OrdinalIgnoreCase))
                {
                    selected = n;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"音声デバイスの取得に失敗しました: {ex.Message}";
        }

        if (selected is not null)
        {
            DeviceCombo.SelectedItem = selected;
        }
        else if (!string.IsNullOrEmpty(preferred))
        {
            DeviceCombo.Text = preferred;
        }
        else if (DeviceCombo.Items.Count > 0)
        {
            DeviceCombo.SelectedIndex = 0;
        }
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        var current = DeviceCombo.Text;
        PopulateDevices(current);
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        LoadFromEnvironment();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var s = CollectValues(out var validationError);
        if (validationError is not null)
        {
            StatusText.Text = validationError;
            return;
        }

        var opts = new SetupStore.SaveOptions(
            SaveLlm: SkipLlm.IsChecked != true,
            SaveVoicevox: SkipVoicevox.IsChecked != true,
            SaveX: SkipX.IsChecked != true);

        if (!opts.SaveLlm && !opts.SaveVoicevox && !opts.SaveX)
        {
            StatusText.Text = "保存する項目がありません (すべてスキップ扱いになっています)。";
            return;
        }

        var report = SetupStore.Save(s, opts);
        if (report.HasErrors)
        {
            StatusText.Text = "保存に失敗しました: " + string.Join(" / ", report.Errors);
            return;
        }

        var msg = string.Join("  |  ", report.Messages);
        StatusText.Text = msg + "  ※ 反映は次回起動する Live/Chat/PostX から。";
    }

    private SetupSettings CollectValues(out string? validationError)
    {
        validationError = null;
        var s = new SetupSettings
        {
            LlmProvider = GetSelectedProvider(),
            AnthropicApiKey = AnthropicKeyBox.Password,
            ClaudeModel = ClaudeModelBox.Text?.Trim() ?? "",
            GeminiApiKey = GeminiKeyBox.Password,
            GeminiModel = GeminiModelBox.Text?.Trim() ?? "",
            OpenAIApiKey = OpenAIKeyBox.Password,
            OpenAIModel = OpenAIModelBox.Text?.Trim() ?? "",

            VoicevoxUrl = VoicevoxUrlBox.Text?.Trim() ?? "",
            OutputDeviceName = (DeviceCombo.Text ?? "").Trim(),

            XConsumerKey = XConsumerKeyBox.Password,
            XConsumerSecret = XConsumerSecretBox.Password,
            XAccessToken = XAccessTokenBox.Password,
            XAccessTokenSecret = XAccessTokenSecretBox.Password,
        };

        if (SkipVoicevox.IsChecked != true)
        {
            if (!int.TryParse(SpeakerIdBox.Text, out int sid) || sid < 0)
            {
                validationError = "話者 ID には 0 以上の整数を入力してください。";
                return s;
            }
            s.SpeakerId = sid;
        }

        return s;
    }
}
