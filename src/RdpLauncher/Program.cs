using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Spectre.Console;

// Configuration management
var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rdp-config.json");
var config = LoadConfig(configPath);

// Get hostname
var hostname = await SelectOrInputAsync(
    "ホスト名を選択または入力してください",
    config.Hostnames,
    "新しいホスト名を入力",
    "ホスト名");

if (string.IsNullOrWhiteSpace(hostname))
{
    AnsiConsole.MarkupLine("[red]ホスト名が入力されていません。[/]");
    return;
}

// Get monitor IDs
var monitorInput = await SelectOrInputAsync(
    "モニターIDを選択または入力してください (例: 0,1,2)",
    config.MonitorConfigs,
    "新しいモニター設定を入力",
    "モニターID (カンマ区切り)");

if (string.IsNullOrWhiteSpace(monitorInput))
{
    AnsiConsole.MarkupLine("[red]モニターIDが入力されていません。[/]");
    return;
}

var monitorIds = monitorInput.Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => s.Trim())
    .ToArray();

if (monitorIds.Length == 0)
{
    AnsiConsole.MarkupLine("[red]有効なモニターIDが入力されていません。[/]");
    return;
}

// Update config
UpdateConfig(config, hostname, monitorInput);
SaveConfig(configPath, config);

// Process RDP file
var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates", "template.rdp");
if (!File.Exists(templatePath))
{
    AnsiConsole.MarkupLine($"[red]テンプレートファイル '{ToDisplayPath(templatePath)}' が見つかりません。[/]");
    return;
}

var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{hostname}-{string.Join("-", monitorIds)}.rdp");
ProcessRdpFile(templatePath, outputPath, hostname, monitorIds);

AnsiConsole.MarkupLine($"[green]RDPファイルを作成しました: {ToDisplayPath(outputPath)}[/]");

// Launch RDP
if (AnsiConsole.Confirm("リモートデスクトップを起動しますか?"))
{
    LaunchRdp(outputPath);
}

return;

// Functions
static string ToDisplayPath(string absolutePath)
{
    var baseDir = AppDomain.CurrentDomain.BaseDirectory;

    try
    {
        // .NET 6+ で利用可能
        var relative = Path.GetRelativePath(baseDir, absolutePath);

        // 見やすさのため、同一ディレクトリなら "./xxx" 形式に寄せる
        if (!string.IsNullOrEmpty(relative) &&
            !relative.StartsWith("..") &&
            !Path.IsPathRooted(relative))
        {
            return $".{Path.DirectorySeparatorChar}{relative}";
        }

        return relative;
    }
    catch
    {
        // 失敗時は絶対パスをそのまま返す（安全策）
        return absolutePath;
    }
}

static RdpConfig LoadConfig(string path)
{
    if (!File.Exists(path))
    {
        return new RdpConfig();
    }

    try
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RdpConfig>(json) ?? new RdpConfig();
    }
    catch
    {
        return new RdpConfig();
    }
}

static void SaveConfig(string path, RdpConfig config)
{
    var json = JsonSerializer.Serialize(config, JsonSerializerOptionsCache.Default);
    File.WriteAllText(path, json);
}

static void UpdateConfig(RdpConfig config, string hostname, string monitorConfig)
{
    if (!config.Hostnames.Contains(hostname))
    {
        config.Hostnames.Add(hostname);
    }

    if (!config.MonitorConfigs.Contains(monitorConfig))
    {
        config.MonitorConfigs.Add(monitorConfig);
    }
}

static async Task<string> SelectOrInputAsync(
    string title,
    List<string> options,
    string newOptionText,
    string inputPrompt)
{
    var choices = new List<string>(options) { newOptionText };

    var selection = await Task.Run(() =>
        AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .PageSize(10)
                .AddChoices(choices)));

    return selection == newOptionText
        ? AnsiConsole.Ask<string>($"[yellow]{inputPrompt}:[/]")
        : selection;
}

static void ProcessRdpFile(
    string templatePath,
    string outputPath,
    string hostname,
    string[] monitorIds)
{
    var lines = File.ReadAllLines(templatePath, Encoding.UTF8);

    var selectedMonitors = string.Join(",", monitorIds);
    var useMultimon = monitorIds.Length > 1 ? 1 : 0;

    // 先頭に固定で配置したいキーと値（定義順が先頭の並び順になります）
    var desired = new (string Key, string Value)[]
    {
        ("selectedmonitors:s:", selectedMonitors),
        ("use multimon:i:", useMultimon.ToString()),
        ("full address:s:", hostname),
    };

    // 1) テンプレートから対象キーの既存行をすべて取り除く
    var bodies = lines
        .Where(line => !desired.Any(d => line.StartsWith(d.Key, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    // 2) 結果: 先頭に desired を定義順で挿入し、その後に残りを続ける
    var result = new List<string>(desired.Length + bodies.Count);
    foreach ((string key, string value) in desired)
    {
        result.Add($"{key}{value}");
    }

    result.AddRange(bodies);

    File.WriteAllLines(outputPath, result, Encoding.UTF8);
}

static void LaunchRdp(string rdpPath)
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"\"{rdpPath}\"",
            UseShellExecute = true
        };

        Process.Start(startInfo);
        AnsiConsole.MarkupLine("[green]リモートデスクトップを起動しました。[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]起動に失敗しました: {ex.Message}[/]");
    }
}

internal static class JsonSerializerOptionsCache
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
    };
}

internal sealed class RdpConfig
{
    public List<string> Hostnames { get; init; } = [];
    public List<string> MonitorConfigs { get; init; } = [];
}
