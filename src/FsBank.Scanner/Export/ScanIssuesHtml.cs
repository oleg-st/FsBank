using System.Net;
using System.Text;
using FsBank.Scanner.Scanning;

namespace FsBank.Scanner.Export;

internal static class ScanIssuesHtml
{
    internal static void Write(string folder, ScanSnapshot snapshot)
    {
        var cards = new StringBuilder();
        foreach (var issue in snapshot.Issues)
        {
            string location = $"{issue.Area}" + (issue.Tab is { } tab ? $" · Bank tab {tab}" : "") + $" · Slot {issue.Row}:{issue.Column}";
            cards.Append($"<article><h2>{Encode(location)}</h2><p class='reason'>{Encode(issue.Reason)}</p>");
            if (issue.Evidence is { Width: > 0, Height: > 0 } evidence)
            {
                cards.Append($"<figure><div class='capture' style='max-width:{evidence.Width}px'><img src='data:image/png;base64,{evidence.ImageBase64}' width='{evidence.Width}' height='{evidence.Height}' alt='{Encode("Item tooltip: " + location)}'>");
                foreach (var line in evidence.Lines)
                {
                    var box = Rectangle.Intersect(line.Box, new(0, 0, evidence.Width, evidence.Height));
                    if (box.Width <= 0 || box.Height <= 0) continue;
                    string tooltip = Describe(line);
                    string position = FormattableString.Invariant($"left:{100.0 * box.X / evidence.Width:F4}%;top:{100.0 * box.Y / evidence.Height:F4}%;width:{100.0 * box.Width / evidence.Width:F4}%;height:{100.0 * box.Height / evidence.Height:F4}%");
                    cards.Append($"<button type='button' class='line' style='{position}' aria-label='{Encode(tooltip)}'><span class='line-tooltip'>{Encode(tooltip)}</span></button>");
                }
                cards.Append("</div><figcaption>" + (evidence.Lines.Length > 0
                    ? "Highlighted lines need review. Hover, tap or focus a line to see the recognized text."
                    : "No specific text line could be identified. Check the capture for missing data.") + "</figcaption></figure>");
                if (evidence.Lines.Length > 0)
                {
                    cards.Append("<details><summary>Recognized text for highlighted lines</summary><ul>");
                    foreach (var line in evidence.Lines) cards.Append($"<li><pre>{Encode(Describe(line))}</pre></li>");
                    cards.Append("</ul></details>");
                }
            }
            else cards.Append("<p class='muted'>No item image is available for this issue.</p>");
            cards.Append("</article>");
        }
        string html = """
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>FsBank scan results</title><style>
            *{box-sizing:border-box}body{font:16px/1.5 system-ui;max-width:1000px;margin:32px auto;padding:0 20px;color:#202a35;background:#f7f8fa}
            a{color:#175da8}h1{margin-bottom:12px}h2{font-size:19px;margin:0 0 12px}article{margin:24px 0;padding:24px;background:white;border:1px solid #dce1e7;border-radius:12px}
            .reason{white-space:pre-wrap;overflow-wrap:anywhere;color:#88331b}figure{margin:20px 0}.capture{position:relative;width:100%;line-height:0}.capture img{display:block;width:100%;height:auto}
            .line{position:absolute;padding:0;border:2px solid #ff654f;background:#ff4d351f;border-radius:2px;cursor:help}
            .line:hover,.line:focus{background:#ff4d3540;border-color:#ffbe63;outline:2px solid #fff;z-index:2}
            .line-tooltip{display:none;position:absolute;top:calc(100% + 8px);left:0;width:max-content;max-width:min(360px,65vw);padding:10px 12px;border-radius:6px;background:#202a35;color:white;box-shadow:0 4px 16px #0005;font:14px/1.45 system-ui;text-align:left;white-space:pre-wrap;overflow-wrap:anywhere;pointer-events:none}
            .line:hover .line-tooltip,.line:focus .line-tooltip{display:block}figcaption,.muted{font-size:14px;color:#5b6673;margin-top:10px}summary{cursor:pointer}pre{font:14px/1.5 system-ui;white-space:pre-wrap;overflow-wrap:anywhere}
            @media(max-width:600px){body{margin:20px auto;padding:0 12px}article{padding:14px}h2{font-size:17px}}
            </style><h1>Scan results</h1>
            """;
        html += $"<p>{Encode(snapshot.Message)}</p><p><a href='items.html'>View items</a></p><p>Recognized without issues: {snapshot.Items} · Needs review: {snapshot.Issues.Length}.</p>";
        html += snapshot.Issues.Length == 0 ? "<p>No issues affecting exported data were detected.</p>" : cards.ToString();
        File.WriteAllText(Path.Combine(folder, "scan-issues.html"), html + "</html>");
    }

    private static string Describe(ScanIssueLine line) => $"Line {line.Index}\nRecognized: {(line.Text.Length == 0 ? "(empty)" : line.Text)}"
        + (line.RawText is { } raw && raw != line.Text ? "\nRaw OCR: " + (raw.Length == 0 ? "(empty)" : raw) : "")
        + "\n" + string.Join("\n", line.Reasons);
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
