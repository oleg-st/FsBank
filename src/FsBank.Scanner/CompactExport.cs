using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner;

internal static class CompactExport
{
    internal static string FullReportPath(string folder) => Path.Combine(folder,
        File.Exists(Path.Combine(folder, "full.json")) ? "full.json" : "items.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ConvertFile(string input)
    {
        if (Directory.Exists(input)) input = FullReportPath(input);
        string folder = Path.GetDirectoryName(Path.GetFullPath(input))!;
        using var document = JsonDocument.Parse(File.ReadAllBytes(input));
        var rows = document.RootElement.GetProperty("items");
        // Preserve the original diagnostics before replacing a legacy items.json.
        if (Path.GetFileName(input).Equals("items.json", StringComparison.OrdinalIgnoreCase))
            File.Move(input, Path.Combine(folder, "full.json"), overwrite: false);
        return Write(folder, rows.EnumerateArray());
    }

    public static string Write(string folder, IEnumerable<JsonElement> rows)
    {
        int? tab = null;
        string locationType = "bank";
        string metadata = Path.Combine(folder, "session.json");
        if (File.Exists(metadata))
        {
            using var session = JsonDocument.Parse(File.ReadAllText(metadata));
            if (session.RootElement.TryGetProperty("LocationType", out var kind))
                locationType = kind.GetString() ?? "bank";
            if (session.RootElement.TryGetProperty("ActiveTab", out var active) && active.ValueKind == JsonValueKind.Number && active.TryGetInt32(out int number) && number > 0)
                tab = number;
        }
        var items = new JsonArray();
        foreach (var row in rows) items.Add(Project(row, tab, locationType));
        string output = Path.Combine(folder, "items.json");
        File.WriteAllText(output, items.ToJsonString(Options));
        return output;
    }

    private static JsonNode? Value(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) ? JsonNode.Parse(value.GetRawText()) : null;

    internal static JsonObject Project(JsonElement row, int? sessionTab, string locationType = "bank")
    {
        if(locationType is not ("bank" or "equipped" or "inventory"))throw new InvalidDataException("Unknown location type: " + locationType);
        row.TryGetProperty("item", out var item);
        var stats = new JsonArray();
        var mods = new JsonArray();
        if (item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("Stats", out var sourceStats))
                foreach (var stat in sourceStats.EnumerateArray())
                    stats.Add(new JsonObject { ["type"] = Value(stat, "Name"), ["value"] = Value(stat, "Value") });
            if (item.TryGetProperty("Modifiers", out var sourceMods))
                foreach (var mod in sourceMods.EnumerateArray())
                {
                    string kind = mod.GetProperty("Kind").GetString() ?? "unresolved";
                    var result = new JsonObject { ["type"] = kind };
                    if (kind == "stat")
                        result["stat"] = new JsonObject { ["type"] = Value(mod, "Name"), ["value"] = Value(mod, "Value") };
                    else if (kind == "unresolved") result["text"] = Value(mod, "Lines");
                    else
                    {
                        result[kind] = Value(mod, "Name");
                        if (Value(mod, "Value") is { } amount) result["value"] = amount;
                    }
                    mods.Add(result);
                }
        }
        string file = Value(row, "file")?.GetValue<string>() ?? "";
        var cell = Regex.Match(file, @"(?:^|[/\\])r(\d+)_c(\d+)\.png$");
        var fileTab = Regex.Match(file, @"(?:^|[/\\])tab-(\d+)[/\\]");
        return new JsonObject
        {
            ["name"] = Value(item, "Name"), ["item_level"] = Value(item, "ItemLevel"),
            ["temper"] = Value(item, "Temperable")?.GetValue<bool>() == false ? JsonValue.Create("non_temperable") :
                Value(item, "TemperedCurrent") is { } current && Value(item, "TemperedMaximum") is { } maximum ? JsonValue.Create($"{current}/{maximum}") : null,
            ["rarity"] = Value(item, "Rarity"), ["stats"] = stats, ["mods"] = mods,
            ["item_slot"] = Value(item, "Slot"),
            ["location"] = new JsonObject
            {
                ["type"] = locationType,
                ["tab"] = locationType != "bank" ? null : Value(row, "tab") ?? (fileTab.Success ? JsonValue.Create(int.Parse(fileTab.Groups[1].Value)) : JsonValue.Create(sessionTab)),
                ["row"] = cell.Success ? JsonValue.Create(int.Parse(cell.Groups[1].Value)) : null,
                ["column"] = cell.Success ? JsonValue.Create(int.Parse(cell.Groups[2].Value)) : null
            }
        };
    }
}
