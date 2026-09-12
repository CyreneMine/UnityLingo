using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnityLingo.Core;

namespace UnityLingo;

public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly HistoryStore history;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private AppSettings settings;
    private CancellationTokenSource? activeRequest;
    private GenerationResult? result;
    private bool loading;

    public MainWindow() : this(SettingsStore.DefaultDirectory) { }
    public MainWindow(string dataDirectory)
    {
        store = new SettingsStore(dataDirectory); history = new HistoryStore(dataDirectory);
        settings = store.Load();
        InitializeComponent(); RefreshSelections();
        if (settings.Profiles.Count == 0) StatusText.Text = "欢迎使用 · 点击右上角“设置”，添加模型配置后开始。";
        Closed += (_, _) => { activeRequest?.Cancel(); http.Dispose(); };
    }
    private void RefreshSelections()
    {
        loading = true;
        ScenarioBox.ItemsSource = settings.Scenarios;
        ScenarioBox.SelectedItem = settings.Scenarios.FirstOrDefault(x => x.Id == settings.SelectedScenarioId) ?? settings.Scenarios[0];
        ModelBox.ItemsSource = settings.Profiles;
        ModelBox.SelectedItem = settings.Profiles.FirstOrDefault(x => x.Id == settings.SelectedProfileId) ?? settings.Profiles.FirstOrDefault();
        loading = false; UpdateMode();
    }
    private void UpdateMode()
    {
        if (NamingBox is null) return;
        var naming = (ScenarioBox.SelectedItem as Scenario)?.OutputKind == OutputKind.Naming;
        NamingBox.Visibility = naming ? Visibility.Visible : Visibility.Collapsed;
        LanguageBox.Visibility = naming ? Visibility.Collapsed : Visibility.Visible;
        ModeLabel.Text = naming ? "命名对象" : "目标语言";
    }
    private void Scenario_Changed(object sender, SelectionChangedEventArgs e) { UpdateMode(); SaveSelection(); }
    private void Model_Changed(object sender, SelectionChangedEventArgs e) => SaveSelection();
    private void SaveSelection()
    {
        if (loading || ModelBox is null || ScenarioBox.SelectedItem is not Scenario scenario) return;
        settings.SelectedScenarioId = scenario.Id;
        settings.SelectedProfileId = (ModelBox.SelectedItem as ModelProfile)?.Id;
        try { store.Save(settings); } catch { StatusText.Text = "无法保存当前选择，请检查本地目录权限。"; }
    }
    private async void Generate_Click(object sender, RoutedEventArgs e) => await GenerateAsync();
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await GenerateAsync(); }
    }
    private async Task GenerateAsync()
    {
        if (activeRequest is not null) return;
        if (ModelBox.SelectedItem is not ModelProfile profile) { StatusText.Text = "请先在设置中添加模型配置。"; return; }
        if (ScenarioBox.SelectedItem is not Scenario scenario || string.IsNullOrWhiteSpace(InputBox.Text)) { StatusText.Text = "请先输入需要处理的文本。"; return; }
        var input = new GenerationInput(InputBox.Text, ContextBox.Text, (NamingKind)NamingBox.SelectedIndex, (TargetLanguage)LanguageBox.SelectedIndex);
        using var cts = new CancellationTokenSource(); activeRequest = cts; SetBusy(true);
        ShowResult(null); StatusText.Text = $"正在使用 {profile.Name} 生成…";
        try
        {
            var generated = await new TranslationClient(http).GenerateAsync(profile, KeyProtection.Unprotect(profile.EncryptedKey), scenario, input, cts.Token);
            cts.Token.ThrowIfCancellationRequested(); ShowResult(generated);
            StatusText.Text = "已完成 · 命名结果可直接复制名称。";
            try { history.Add(input.Text, input.Context, scenario.Name, $"{profile.Name} / {profile.ModelId}", generated, settings.HistoryEnabled, settings.HistoryLimit); }
            catch { StatusText.Text = "已生成，但历史记录保存失败；你仍可复制结果。"; }
        }
        catch (OperationCanceledException) { StatusText.Text = "已取消本地等待 · 服务端可能已处理该请求。"; }
        catch (LingoException ex) { StatusText.Text = ex.Message; }
        catch (System.Security.Cryptography.CryptographicException) { StatusText.Text = "无法解密 API Key，请在设置中重新填写。"; }
        catch { StatusText.Text = "生成失败，请检查模型配置和本地环境后重试。"; }
        finally { activeRequest = null; SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        GenerateButton.IsEnabled = SettingsButton.IsEnabled = HistoryButton.IsEnabled = OptionsPanel.IsEnabled = InputBox.IsEnabled = ContextBox.IsEnabled = !busy;
        CancelButton.IsEnabled = busy; BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShowResult(GenerationResult? value)
    {
        result = value; EmptyHint.Visibility = value is null ? Visibility.Visible : Visibility.Collapsed;
        TranslationOutput.Visibility = value is not null && value.Naming is null ? Visibility.Visible : Visibility.Collapsed;
        NamingOutput.Visibility = value?.Naming is not null ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = value is not null; CopyButton.Content = value?.Naming is not null ? "复制推荐名" : "复制结果";
        if (value?.Naming is { } naming)
            Suggestions.ItemsSource = naming.Alternatives.Prepend(naming.Recommended).Select((x, i) => new { Label = i == 0 ? "推荐名称" : $"备选 {i}", x.Name, x.Explanation }).ToArray();
        else TranslationOutput.Text = value?.Text ?? "";
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => activeRequest?.Cancel();
    private void Copy_Click(object sender, RoutedEventArgs e) => Copy(result?.Naming?.Recommended.Name ?? result?.Text);
    private void CopyName_Click(object sender, RoutedEventArgs e) => Copy((sender as Button)?.Tag as string);
    private void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); StatusText.Text = "已复制到剪贴板。"; } catch { StatusText.Text = "剪贴板暂时不可用，请稍后重试。"; }
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(settings, store, history, new TranslationClient(http)) { Owner = this };
        if (dialog.ShowDialog() == true) { settings = dialog.Settings; RefreshSelections(); StatusText.Text = "设置已保存。"; }
    }
    private void History_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new HistoryWindow(history, settings.HistoryLimit) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedEntry is { } entry)
            {
                InputBox.Text = entry.Input; ContextBox.Text = entry.Context; ShowResult(entry.Result);
                StatusText.Text = $"历史结果 · {entry.Scenario} · {entry.Model} · {entry.CreatedAt.ToLocalTime():g}";
            }
        }
        catch { StatusText.Text = "无法读取历史记录，请检查本地数据库。"; }
    }
}
