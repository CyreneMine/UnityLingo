using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UnityLingo;
using UnityLingo.Core;

internal static class Program
{
    private static int passed;
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), "UnityLingoTests", Guid.NewGuid().ToString("N"));
    [STAThread]
    private static int Main()
    {
        try
        {
            Directory.CreateDirectory(TestDirectory);
            ApiTests().GetAwaiter().GetResult();
            StorageTests(); UiTests();
            Console.WriteLine($"PASS: {passed} checks. Temporary data: {TestDirectory}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAILED: " + name); passed++; Console.WriteLine("PASS " + name); }
    private static async Task Reject(Func<Task> action, string contains)
    {
        try { await action(); throw new Exception("Expected error " + contains); }
        catch (LingoException ex) { Check(ex.Message.Contains(contains), "error: " + contains); }
    }
    private static string Naming(string a = "moveSpeed", string b = "movementSpeed", string c = "travelSpeed") => JsonSerializer.Serialize(new
    {
        recommended = new { name = a, explanation = "角色移动速度字段" },
        alternatives = new[] { new { name = b, explanation = "强调移动过程" }, new { name = c, explanation = "强调行进速度" } }
    });
    private static HttpResponseMessage Response(string content, string reason = "stop") => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = reason, message = new { content } } } }), Encoding.UTF8, "application/json") };
    private static async Task ApiTests()
    {
        var profile = new ModelProfile { Name = "Mock", BaseUrl = "https://example.test/custom/v1/", ModelId = "test-model" };
        var scenario = BuiltIns.Create()[2]; var input = new GenerationInput("移动速度", "角色 float 字段", NamingKind.Field, TargetLanguage.Auto);
        string? body = null; Uri? uri = null; string? auth = null; var calls = 0;
        var handler = new FakeHandler(async (request, token) =>
        {
            calls++; uri = request.RequestUri; auth = request.Headers.Authorization?.ToString();
            body = await request.Content!.ReadAsStringAsync(token); return Response(Naming());
        });
        using var http = new HttpClient(handler); var client = new TranslationClient(http);
        var result = await client.GenerateAsync(profile, "test-key-placeholder", scenario, input, default);
        Check(result.Naming?.Recommended.Name == "moveSpeed", "recommended naming");
        Check(uri?.AbsoluteUri == "https://example.test/custom/v1/chat/completions", "Base URL path and slash");
        Check(auth == "Bearer test-key-placeholder", "bearer header");
        using (var payload = JsonDocument.Parse(body!))
        {
            Check(payload.RootElement.GetProperty("model").GetString() == "test-model", "model ID");
            var messages = payload.RootElement.GetProperty("messages");
            Check(messages.GetArrayLength() == 2 && messages[0].GetProperty("content").GetString()!.Contains(scenario.Prompt), "scenario on each request; no history");
            var user = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            Check(user.RootElement.GetProperty("text").GetString() == input.Text && user.RootElement.GetProperty("context").GetString() == input.Context, "input and context preserved");
        }
        profile.ModelId = "second-model"; await client.TestConnectionAsync(profile, "second-placeholder", default);
        Check(body!.Contains("second-model") && auth == "Bearer second-placeholder", "switch model and key");
        foreach (var kind in Enum.GetValues<NamingKind>())
        {
            var lower = kind is NamingKind.Field or NamingKind.LocalVariable;
            var value = lower ? Naming() : Naming("MoveSpeed", "MovementSpeed", "TravelSpeed");
            Check(NamingValidator.Parse(value, kind).Alternatives.Count == 2, "naming kind " + kind);
        }
        Check(NamingValidator.Parse(Naming("isDead", "hasDied", "isDeceased"), NamingKind.Field).Recommended.Name == "isDead", "boolean example");
        Check(NamingValidator.Parse(Naming("TakeDamage", "ReceiveDamage", "ApplyDamage"), NamingKind.Method).Recommended.Name == "TakeDamage", "method example");
        foreach (var value in new[] { "invalid json", Naming("class"), Naming("1speed"), Naming("move-speed"), Naming("MoveSpeed"), Naming("moveSpeed", "moveSpeed"), "{}", Naming("移动速度"), Naming("@class"), Naming("moveSpeed\n") })
            await Reject(() => Task.FromResult(NamingValidator.Parse(value, NamingKind.Field)), "命名结果格式");
        foreach (var url in new[] { "ftp://example.test", "https://example.test/v1?key=secret", "http://example.test/v1", "https://example.test/v1/chat/completions" })
            await Reject(() => Task.FromResult(TranslationClient.Endpoint(url)), "Base URL");
        Check(TranslationClient.Endpoint("http://localhost:11434/v1").Port == 11434, "local HTTP allowed");
        foreach (var language in Enum.GetValues<TargetLanguage>())
        {
            var prompt = TranslationClient.SystemPrompt(BuiltIns.Create()[1], input with { TargetLanguage = language });
            Check(prompt.Contains("API") && prompt.Contains("Markdown") && prompt.Contains(language switch { TargetLanguage.Chinese => "目标语言为简体中文", TargetLanguage.English => "目标语言为英文", _ => "自动识别" }), "document prompt " + language);
        }
        const string markdown = "# Title\n```csharp\ntransform.Translate(Vector3.up);\n```\n";
        handler.Action = (_, _) => Task.FromResult(Response(markdown));
        Check((await client.GenerateAsync(profile, "test", BuiltIns.Create()[1], input, default)).Text == markdown, "translation whitespace and code preservation");
        foreach (var status in new[] { 400, 401, 403, 404, 429, 500 })
        {
            handler.Action = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("sensitive service error") });
            await Reject(() => client.GenerateAsync(profile, "test", scenario, input, default), status.ToString());
        }
        handler.Action = (_, _) => throw new HttpRequestException("secret details");
        await Reject(() => client.GenerateAsync(profile, "test", scenario, input, default), "无法连接");
        handler.Action = (_, _) => throw new TaskCanceledException();
        await Reject(() => client.GenerateAsync(profile, "test", scenario, input, default), "超时");
        handler.Action = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("never"); };
        using (var cts = new CancellationTokenSource(30))
        {
            try { await client.GenerateAsync(profile, "test", scenario, input, cts.Token); throw new Exception("Cancellation missing"); }
            catch (OperationCanceledException) { Check(true, "user cancellation"); }
        }
        handler.Action = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await Reject(() => client.GenerateAsync(profile, "test", scenario, input, default), "格式");
        handler.Action = (_, _) => Task.FromResult(Response("cut off", "length"));
        await Reject(() => client.GenerateAsync(profile, "test", scenario, input, default), "截断");
        handler.Action = (_, _) => { calls++; return Task.FromResult(Response("bad naming")); };
        var before = calls;
        await Reject(() => client.GenerateAsync(profile, "test", scenario, input, default), "命名结果格式");
        Check(calls == before + 1, "no automatic paid retry");
    }
    private static void StorageTests()
    {
        var settingsStore = new SettingsStore(TestDirectory);
        Check(settingsStore.Load().HistoryLimit == 200, "default visible history limit");
        var encrypted = KeyProtection.Protect("test-key-placeholder");
        Check(!encrypted.Contains("test-key-placeholder") && KeyProtection.Unprotect(encrypted) == "test-key-placeholder", "DPAPI round trip");
        var settings = new AppSettings { HistoryLimit = 350, SelectedProfileId = "test", Profiles = [new() { Id = "test", EncryptedKey = encrypted }] };
        settings.Scenarios.Add(new() { Name = "自定义", Prompt = "Custom" }); settingsStore.Save(settings);
        var reloaded = settingsStore.Load();
        Check(reloaded.HistoryLimit == 350 && reloaded.Scenarios.Count == 4 && reloaded.SelectedProfileId == "test", "settings restart persistence");
        Check(!File.ReadAllText(Path.Combine(TestDirectory, "settings.json")).Contains("test-key-placeholder"), "no plaintext key on disk");
        var clone = SettingsStore.Clone(settings); clone.Scenarios[0].Prompt = "changed";
        Check(settings.Scenarios[0].Prompt != "changed", "cancel settings isolation");
        var history = new HistoryStore(TestDirectory);
        for (var i = 0; i < 205; i++) history.Add("输入 " + i, "context", "Unity", "mock", new GenerationResult("result " + i), true, 203);
        Check(history.Count() == 203 && history.Search("")[0].Input == "输入 204", "adjustable cap above 200 and newest ordering");
        Check(history.Search("输入 0").Count == 0 && history.Search("result 204").Count == 1, "oldest trimming and search result");
        Check(history.Search("", 100).Count == 100 && history.Search("", 200).Count == 3, "history pagination");
        history.Add("disabled", "", "", "", new GenerationResult("none"), false, 203);
        Check(history.Count() == 203, "disabled preserves existing history");
        Check(new HistoryStore(TestDirectory).Count() == 203, "SQLite restart persistence");
        history.Trim(2); Check(history.Count() == 2, "lower cap trimming");
        history.Delete(history.Search("")[0].Id); Check(history.Count() == 1, "single delete");
        var named = new GenerationResult(Naming(), NamingValidator.Parse(Naming(), NamingKind.Field));
        history.Add("移动速度", "float", "命名", "mock", named, true, 10);
        Check(history.Search("移动速度")[0].Result.Naming?.Recommended.Name == "moveSpeed", "naming history serialization");
        Check(history.Search("强调行进").Count == 1, "search Chinese result explanation");
        Check(history.Search("' OR 1=1 --").Count == 0, "parameterized search");
        history.Clear(); Check(history.Count() == 0, "clear all");
    }
    private static void UiTests()
    {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var uiDir = Path.Combine(TestDirectory, "ui");
        new SettingsStore(uiDir).Save(new AppSettings { Profiles = [new() { Name = "我的模型", ModelId = "example-model" }] });
        var window = new MainWindow(uiDir);
        window.Measure(new Size(1120, 780)); window.Arrange(new Rect(0, 0, 1120, 780)); window.UpdateLayout();
        var scene = (ComboBox)window.FindName("ScenarioBox");
        Check(((Scenario)scene.SelectedItem).Id == "unity-naming", "default naming scene");
        scene.SelectedIndex = 0;
        Check(((ComboBox)window.FindName("LanguageBox")).Visibility == Visibility.Visible && ((ComboBox)window.FindName("NamingBox")).Visibility == Visibility.Collapsed, "translation UI switching");
        scene.SelectedIndex = 2;
        Check(((ComboBox)window.FindName("NamingBox")).SelectedIndex == 1, "default field naming");
        ((TextBox)window.FindName("InputBox")).Text = "移动速度";
        ((TextBox)window.FindName("ContextBox")).Text = "角色的移动速度，float 字段";
        typeof(MainWindow).GetMethod("ShowResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window,
            [new GenerationResult(Naming(), NamingValidator.Parse(Naming(), NamingKind.Field))]);
        Check(((ItemsControl)window.FindName("Suggestions")).Items.Count == 3, "three naming cards");
        var artifacts = Path.GetFullPath(Path.Combine("artifacts", "qa")); Directory.CreateDirectory(artifacts);
        Render(window, Path.Combine(artifacts, "main.png"), 1120, 780);
        var settings = new SettingsWindow(new AppSettings(), new SettingsStore(uiDir), new HistoryStore(uiDir), new TranslationClient(new HttpClient()));
        typeof(SettingsWindow).GetMethod("AddModel_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(settings, [settings, new RoutedEventArgs()]);
        var profile = (ModelProfile)((ListBox)settings.FindName("Models")).SelectedItem;
        profile.Name = "我的兼容模型"; profile.ModelId = "your-model-id";
        ((PasswordBox)settings.FindName("ApiKey")).Password = "test-key-placeholder";
        Check(KeyProtection.Unprotect(profile.EncryptedKey) == "test-key-placeholder", "settings password editing encrypts immediately");
        Render(settings, Path.Combine(artifacts, "models.png"), 880, 740);
        ((TabControl)settings.FindName("Tabs")).SelectedIndex = 1;
        Render(settings, Path.Combine(artifacts, "scenarios.png"), 880, 740);
        Check(((TextBox)settings.FindName("HistoryLimit")).Text == "200", "history cap displayed in settings");
        ((TabControl)settings.FindName("Tabs")).SelectedIndex = 2;
        Render(settings, Path.Combine(artifacts, "settings.png"), 880, 740);
        var history = new HistoryWindow(new HistoryStore(uiDir), 350);
        Render(history, Path.Combine(artifacts, "history.png"), 980, 700);
        Check(((TextBlock)history.FindName("Status")).Text.Contains("350"), "current limit displayed in history");
        history.Close(); settings.Close(); window.Close(); app.Shutdown();
    }
    private static void Render(Window window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Clear(); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
        Check(new FileInfo(path).Length > 1000, "WPF layout render " + Path.GetFileName(path));
    }
}

internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Action { get; set; } = action;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Action(request, cancellationToken);
}
