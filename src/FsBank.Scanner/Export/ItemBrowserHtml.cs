namespace FsBank.Scanner.Export;

internal static class ItemBrowserHtml
{
    private const string Placeholder = "__ITEM_DATA__";
    private static readonly Lazy<string> Template = new(LoadTemplate);

    public static void Write(string path, string json)
    {
        // JSON is embedded in a script element, so HTML delimiters must be escaped.
        string payload = json.Replace("&", "\\u0026").Replace("<", "\\u003c").Replace(">", "\\u003e");
        File.WriteAllText(path, Template.Value.Replace(Placeholder, payload));
    }

    private static string LoadTemplate()
    {
        using var stream = typeof(ItemBrowserHtml).Assembly.GetManifestResourceStream("FsBank.ItemBrowserTemplate")
            ?? throw new InvalidOperationException("Item browser template is missing from the application.");
        using var reader = new StreamReader(stream);
        string template = reader.ReadToEnd();
        int index = template.IndexOf(Placeholder, StringComparison.Ordinal);
        if (index < 0 || template.IndexOf(Placeholder, index + Placeholder.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Item browser template must contain exactly one data placeholder.");
        return template;
    }
}
