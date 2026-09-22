using System.Text.Encodings.Web;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Export;

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

    public static string Write(string folder, IEnumerable<JsonElement> rows, string? imageRoot = null)
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
        var cards=new StringBuilder();
        foreach(var row in rows)
        {
            var item=Project(row,tab,locationType);
            items.Add(item);
            string file=Value(row,"file")?.GetValue<string>() ?? "";
            cards.Append("<article><div class='pair'>");
            if(imageRoot is not null && file.Length>0)
            {
                string image=Path.Combine(imageRoot,file.Replace('/',Path.DirectorySeparatorChar));
                if(File.Exists(image))cards.Append("<img src='data:image/png;base64,").Append(System.Convert.ToBase64String(File.ReadAllBytes(image))).Append("' alt='Item tooltip'>");
            }
            else if(file.Length>0)cards.Append("<img src='").Append(WebUtility.HtmlEncode(string.Join("/",file.Replace('\\','/').Split('/').Select(Uri.EscapeDataString)))).Append("' alt='Item tooltip'>");
            cards.Append("<pre>").Append(WebUtility.HtmlEncode(item.ToJsonString(Options))).Append("</pre></div></article>");
        }
        File.WriteAllText(Path.Combine(folder,"items.html"),"<!doctype html><html lang='en'><meta charset='utf-8'><title>FsBank items</title><style>body{background:#141c25;color:#eee;font:15px system-ui;margin:24px}a{color:#8bd0ff}article{border-top:1px solid #789;padding:20px 0}.pair{display:flex;gap:20px}img{max-width:45%;object-fit:contain;align-self:start}pre{white-space:pre-wrap;overflow-wrap:anywhere;min-width:0}@media(max-width:700px){.pair{display:block}img{max-width:100%}}</style><h1>Items</h1><p><a href='items.json'>items.json</a>"+(imageRoot is null ? " | <a href='report.html'>Full report</a>" : "")+"</p><p>"+items.Count+" items. Results require visual review.</p>"+cards+"</html>");
        string output = Path.Combine(folder, "items.json");
        File.WriteAllText(output, items.ToJsonString(Options));
        return output;
    }

    private static JsonNode? Value(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) ? JsonNode.Parse(value.GetRawText()) : null;

    internal static JsonObject Project(JsonElement row, int? sessionTab, string locationType = "bank")
    {
        locationType=Value(row,"location_type")?.GetValue<string>() ?? locationType;
        if(locationType is not ("bank" or "equipped" or "inventory"))throw new InvalidDataException("Unknown location type: " + locationType);
        row.TryGetProperty("item", out var item);
        var stats = new JsonArray();
        var mods = new JsonArray();
        if (item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("Stats", out var sourceStats))
                foreach (var stat in sourceStats.EnumerateArray())
                {
                    string? origin = Value(stat, "Origin")?.GetValue<string>();
                    stats.Add(new JsonObject
                    {
                        ["type"] = Value(stat, "Name"), ["value"] = Value(stat, "Value"),
                        ["origin"] = origin switch
                        {
                            "base" => "base",
                            "dynamic" or "fixed_roll" => "dynamic",
                            _ => "unresolved"
                        }
                    });
                }
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
                        result[kind] = kind == "set" && Value(mod, "Name") is { } setName
                            ? JsonValue.Create(Regex.Replace(setName.GetValue<string>(), @"^[^\p{L}]+", ""))
                            : Value(mod, "Name");
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
