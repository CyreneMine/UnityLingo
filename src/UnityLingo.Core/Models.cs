namespace UnityLingo.Core;

public enum OutputKind { Translation, Naming }
public enum NamingKind { LocalVariable, Field, Property, Method, Class }
public enum TargetLanguage { Auto, Chinese, English }

public sealed class ModelProfile : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private string name = "新模型";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => name; set { name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ModelId { get; set; } = "";
    public string EncryptedKey { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class Scenario : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private string name = "新场景";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => name; set { name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string Prompt { get; set; } = "";
    public OutputKind OutputKind { get; set; }
    public bool IsBuiltIn => BuiltIns.Create().Any(x => x.Id == Id);
    public override string ToString() => Name;
}

public static class BuiltIns
{
    public static List<Scenario> Create() =>
    [
        new() { Id = "translation", Name = "中英翻译", Prompt = "你是一位专业中英翻译。忠实准确地翻译，表达自然，保留原文语气。", OutputKind = OutputKind.Translation },
        new() { Id = "unity-docs", Name = "Unity 开发文档", Prompt = "你是一位熟悉 Unity 和 C# 的技术翻译。使用开发者常用术语，保留代码块、代码、API 名称、标识符、链接及 Markdown 格式。不要翻译代码。", OutputKind = OutputKind.Translation },
        new() { Id = "unity-naming", Name = "Unity C# 命名", Prompt = "你是一位资深 Unity C# 开发者。根据中文意图和使用上下文，推荐清楚、简洁、符合 C# 语义的英文标识符，不做逐字翻译。布尔值使用 is/has/can 等合适表达，方法通常以动词开头。含义不明确时，在中文说明中明确采用的理解。", OutputKind = OutputKind.Naming }
    ];
}

public sealed class AppSettings
{
    public List<ModelProfile> Profiles { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = BuiltIns.Create();
    public string? SelectedProfileId { get; set; }
    public string SelectedScenarioId { get; set; } = "unity-naming";
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryLimit { get; set; } = 200;
}

public sealed record GenerationInput(string Text, string Context, NamingKind NamingKind, TargetLanguage TargetLanguage);
public sealed record NameSuggestion(string Name, string Explanation);
public sealed record NamingResult(NameSuggestion Recommended, List<NameSuggestion> Alternatives);
public sealed record GenerationResult(string Text, NamingResult? Naming = null);
public sealed record HistoryEntry(long Id, DateTimeOffset CreatedAt, string Input, string Context,
    string Scenario, string Model, GenerationResult Result)
{
    public string Summary => $"{CreatedAt.ToLocalTime():MM-dd HH:mm} · {Scenario} · {Input.Replace('\n', ' ').Replace('\r', ' ')}";
}
