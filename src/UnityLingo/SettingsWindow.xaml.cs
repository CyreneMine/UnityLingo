using System.Windows;
using System.Windows.Controls;
using UnityLingo.Core;

namespace UnityLingo;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; }
    private readonly SettingsStore store;
    private readonly HistoryStore history;
    private readonly TranslationClient client;
    private readonly int originalLimit;
    private bool loadingKey;
    private CancellationTokenSource? testRequest;
    public SettingsWindow(AppSettings original, SettingsStore store, HistoryStore history, TranslationClient client)
    {
        Settings = SettingsStore.Clone(original); this.store = store; this.history = history; this.client = client;
        originalLimit = original.HistoryLimit;
        InitializeComponent();
        OutputType.ItemsSource = new Dictionary<OutputKind, string> { [OutputKind.Translation] = "翻译文本", [OutputKind.Naming] = "C# 命名结果" };
        RefreshModels(Settings.SelectedProfileId); RefreshScenarios(Settings.SelectedScenarioId);
        HistoryEnabled.IsChecked = Settings.HistoryEnabled; HistoryLimit.Text = Settings.HistoryLimit.ToString();
        DataPath.Text = SettingsStore.DefaultDirectory;
        Closed += (_, _) => testRequest?.Cancel();
    }
    private void RefreshModels(string? id = null)
    {
        Models.ItemsSource = null; Models.ItemsSource = Settings.Profiles;
        Models.SelectedItem = Settings.Profiles.FirstOrDefault(x => x.Id == id) ?? Settings.Profiles.FirstOrDefault();
    }
    private void RefreshScenarios(string? id = null)
    {
        Scenarios.ItemsSource = null; Scenarios.ItemsSource = Settings.Scenarios;
        Scenarios.SelectedItem = Settings.Scenarios.FirstOrDefault(x => x.Id == id) ?? Settings.Scenarios.FirstOrDefault();
    }
    private void Model_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ModelForm is null) return;
        ModelForm.DataContext = Models.SelectedItem; ModelForm.IsEnabled = Models.SelectedItem is not null;
        loadingKey = true;
        try { ApiKey.Password = Models.SelectedItem is ModelProfile profile ? KeyProtection.Unprotect(profile.EncryptedKey) : ""; }
        catch { ApiKey.Password = ""; Status.Text = "当前 Key 无法解密，请重新填写后保存。"; }
        finally { loadingKey = false; }
    }
    private void Key_Changed(object sender, RoutedEventArgs e)
    {
        if (!loadingKey && Models.SelectedItem is ModelProfile profile)
        {
            try { profile.EncryptedKey = KeyProtection.Protect(ApiKey.Password); }
            catch { Status.Text = "无法加密 Key，请检查 Windows 用户环境。"; }
        }
    }
    private void AddModel_Click(object sender, RoutedEventArgs e) { var profile = new ModelProfile(); Settings.Profiles.Add(profile); RefreshModels(profile.Id); }
    private void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (Models.SelectedItem is ModelProfile profile && MessageBox.Show(this, $"删除模型配置“{profile.Name}”？", "删除配置", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { Settings.Profiles.Remove(profile); RefreshModels(); }
    }
    private void Scenario_Selected(object sender, SelectionChangedEventArgs e)
    { if (ScenarioForm is not null) { ScenarioForm.DataContext = Scenarios.SelectedItem; ScenarioForm.IsEnabled = Scenarios.SelectedItem is not null; } }
    private void AddScenario_Click(object sender, RoutedEventArgs e) { var scenario = new Scenario { Prompt = "请根据使用上下文准确翻译。" }; Settings.Scenarios.Add(scenario); RefreshScenarios(scenario.Id); }
    private void DuplicateScenario_Click(object sender, RoutedEventArgs e)
    {
        if (Scenarios.SelectedItem is not Scenario source) return;
        var copy = new Scenario { Name = source.Name + " 副本", Prompt = source.Prompt, OutputKind = source.OutputKind };
        Settings.Scenarios.Add(copy); RefreshScenarios(copy.Id);
    }
    private void DeleteScenario_Click(object sender, RoutedEventArgs e)
    {
        if (Scenarios.SelectedItem is not Scenario scenario) return;
        if (scenario.IsBuiltIn) { Status.Text = "内置场景可以编辑或恢复默认；只有自定义场景可以删除。"; return; }
        if (MessageBox.Show(this, $"删除场景“{scenario.Name}”？", "删除场景", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { Settings.Scenarios.Remove(scenario); RefreshScenarios(); }
    }
    private void ResetScenario_Click(object sender, RoutedEventArgs e)
    {
        if (Scenarios.SelectedItem is not Scenario scenario) return;
        var original = BuiltIns.Create().FirstOrDefault(x => x.Id == scenario.Id);
        if (original is null) { Status.Text = "自定义场景没有内置默认值。"; return; }
        if (MessageBox.Show(this, "恢复此场景的默认名称、类型和提示词？", "恢复默认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Settings.Scenarios[Settings.Scenarios.IndexOf(scenario)] = original; RefreshScenarios(original.Id);
    }
    private static void ValidateProfile(ModelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.ModelId)) throw new LingoException("请填写每个模型的配置名称和模型 ID，或删除未完成的配置。");
        TranslationClient.Endpoint(profile.BaseUrl);
        var key = KeyProtection.Unprotect(profile.EncryptedKey);
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl)) throw new LingoException("请填写有效的 API Key，或删除未完成的模型配置。");
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (testRequest is not null || Models.SelectedItem is not ModelProfile profile) return;
        using var cts = new CancellationTokenSource(); testRequest = cts;
        // Keep the cancellation button usable while preventing edits and a second test.
        Models.IsEnabled = SaveButton.IsEnabled = false; ((Button)sender).IsEnabled = false; CancelTest.IsEnabled = true;
        Status.Text = "正在测试连接…";
        try { ValidateProfile(profile); await client.TestConnectionAsync(profile, KeyProtection.Unprotect(profile.EncryptedKey), cts.Token); cts.Token.ThrowIfCancellationRequested(); Status.Text = "连接成功，模型已返回文本。配置尚需点击保存。"; }
        catch (OperationCanceledException) { Status.Text = "已取消测试。服务端可能已处理请求。"; }
        catch (LingoException ex) { Status.Text = ex.Message; }
        catch { Status.Text = "测试失败，请重新填写 Key 并检查配置。"; }
        finally { testRequest = null; Models.IsEnabled = SaveButton.IsEnabled = true; ((Button)sender).IsEnabled = true; CancelTest.IsEnabled = false; }
    }
    private void CancelTest_Click(object sender, RoutedEventArgs e) => testRequest?.Cancel();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(HistoryLimit.Text.Trim(), out var limit) || limit < 1) throw new LingoException("历史记录上限请输入正整数（1–2,147,483,647）。");
            foreach (var profile in Settings.Profiles) ValidateProfile(profile);
            if (Settings.Scenarios.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Prompt))) throw new LingoException("每个场景都需要名称和提示词。");
            var remove = Math.Max(0, history.Count() - limit);
            if (limit < originalLimit && remove > 0 && MessageBox.Show(this, $"新的上限为 {limit} 条，将永久删除最旧的 {remove} 条历史。是否保存？", "调整历史上限", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Settings.HistoryEnabled = HistoryEnabled.IsChecked == true; Settings.HistoryLimit = limit;
            // Save first: a disk-write failure must not delete history.
            store.Save(Settings);
            try { history.Trim(limit); }
            catch { MessageBox.Show(this, "设置已保存，但旧历史暂未清理。下次保存设置或生成时会再次按上限清理。", "历史清理失败"); }
            DialogResult = true;
        }
        catch (LingoException ex) { Status.Text = ex.Message; }
        catch { Status.Text = "保存失败，请检查 Key 是否能解密，以及本地目录是否可写。"; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
