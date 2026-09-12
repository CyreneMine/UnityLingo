using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace UnityLingo.Core;

public static class KeyProtection
{
    public static string Protect(string key) => Convert.ToBase64String(ProtectedData.Protect(
        Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string encrypted) => string.IsNullOrEmpty(encrypted) ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
}

public sealed class SettingsStore(string directory)
{
    private readonly string path = Path.Combine(directory, "settings.json");
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityLingo");
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("设置文件为空。");
        if (settings.HistoryLimit < 1 || settings.Profiles is null || settings.Scenarios is null ||
            settings.Scenarios.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id)) ||
            settings.Profiles.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id)))
            throw new InvalidDataException("设置文件格式不正确，请备份后修复。");
        foreach (var builtIn in BuiltIns.Create())
            if (!settings.Scenarios.Any(x => x.Id == builtIn.Id)) settings.Scenarios.Add(builtIn);
        return settings;
    }
    public void Save(AppSettings settings)
    {
        if (settings.HistoryLimit < 1) throw new ArgumentOutOfRangeException(nameof(settings.HistoryLimit));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
    public static AppSettings Clone(AppSettings settings) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
}

public sealed class HistoryStore
{
    private readonly string connectionString;
    public HistoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "history.db") }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS history (
                id INTEGER PRIMARY KEY AUTOINCREMENT, created TEXT NOT NULL,
                input TEXT NOT NULL, context TEXT NOT NULL, scenario TEXT NOT NULL,
                model TEXT NOT NULL, result TEXT NOT NULL);
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public long Count()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history";
        return (long)cmd.ExecuteScalar()!;
    }
    public void Add(string input, string context, string scenario, string model, GenerationResult result, bool enabled, int limit)
    {
        if (!enabled) return;
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO history(created,input,context,scenario,model,result) VALUES($date,$input,$context,$scenario,$model,$result)";
        cmd.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$input", input); cmd.Parameters.AddWithValue("$context", context);
        cmd.Parameters.AddWithValue("$scenario", scenario); cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$result", JsonSerializer.Serialize(result, new JsonSerializerOptions
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); cmd.ExecuteNonQuery();
        Trim(c, tx, limit); tx.Commit();
    }
    public void Trim(int limit)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        using var c = Open(); using var tx = c.BeginTransaction(); Trim(c, tx, limit); tx.Commit();
    }
    private static void Trim(SqliteConnection c, SqliteTransaction tx, int limit)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT $limit)";
        cmd.Parameters.AddWithValue("$limit", limit); cmd.ExecuteNonQuery();
    }
    public List<HistoryEntry> Search(string query, int offset = 0, int pageSize = 100)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id,created,input,context,scenario,model,result FROM history
            WHERE instr(lower(input),lower($q)) > 0 OR instr(lower(result),lower($q)) > 0
               OR instr(lower(scenario),lower($q)) > 0
            ORDER BY id DESC LIMIT $size OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$q", query); cmd.Parameters.AddWithValue("$size", pageSize); cmd.Parameters.AddWithValue("$offset", offset);
        using var r = cmd.ExecuteReader(); var rows = new List<HistoryEntry>();
        while (r.Read()) rows.Add(new(r.GetInt64(0), DateTimeOffset.Parse(r.GetString(1)), r.GetString(2), r.GetString(3),
            r.GetString(4), r.GetString(5), JsonSerializer.Deserialize<GenerationResult>(r.GetString(6))!));
        return rows;
    }
    public void Delete(long id)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM history WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
    }
    public void Clear()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM history"; cmd.ExecuteNonQuery();
    }
}
