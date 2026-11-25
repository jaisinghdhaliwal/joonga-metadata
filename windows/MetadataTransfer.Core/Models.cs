using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetadataTransfer.Core;

public sealed record Match(string? Original, string? Export, string Status)
{
    public bool Matched => Original is not null && Export is not null && Status == "Matched";
}
public sealed record Workflow(string Id, string Name, string Category, string[] Operations);
public sealed record WorkflowArchive(string Format, int Version, Workflow[] Presets);
public sealed record OperationResult(string Path, bool Success, string Output);
public sealed record GeoPoint(string Path, double Latitude, double Longitude);
public sealed record FavouriteLocation(string Name, double Latitude, double Longitude);

public static partial class WorkflowRules
{
    [GeneratedRegex(@"^-[A-Za-z][A-Za-z0-9:#-]*(=|<|>)")]
    private static partial Regex Assignment();
    public static string[] Parse(string text) => text.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    public static bool Valid(string operation) => Assignment().IsMatch(operation) && !operation.Contains('\n') && !operation.Contains('\r') && !operation.Contains('\0');
    public static void Validate(Workflow w)
    {
        if (string.IsNullOrWhiteSpace(w.Name) || w.Operations is null || w.Operations.Length == 0 || w.Operations.Any(x => !Valid(x)))
            throw new InvalidDataException($"“{w.Name}” needs a name and valid -Tag=Value or -Destination<Source operations.");
    }
}

public sealed class WorkflowStore(string path)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string Path { get; } = path;
    public static Workflow[] Decode(string text)
    {
        using var doc = JsonDocument.Parse(text);
        Workflow[] items;
        if (doc.RootElement.ValueKind == JsonValueKind.Array) items = JsonSerializer.Deserialize<Workflow[]>(text, Json) ?? [];
        else
        {
            var archive = JsonSerializer.Deserialize<WorkflowArchive>(text, Json) ?? throw new InvalidDataException("Empty workflow library.");
            if (archive.Format != "MetadataTransferWorkflows" || archive.Version != 1) throw new InvalidDataException("Unsupported workflow format/version.");
            items = archive.Presets ?? throw new InvalidDataException("Missing presets.");
        }
        foreach (var w in items) WorkflowRules.Validate(w);
        // IDs from external libraries are not trusted to be unique.
        var ids = new HashSet<string>();
        return items.Select(w => w with { Id = string.IsNullOrEmpty(w.Id) || !ids.Add(w.Id) ? Guid.NewGuid().ToString() : w.Id }).ToArray();
    }
    public Workflow[] Load() => File.Exists(Path) ? Decode(File.ReadAllText(Path)) : [];
    public static void Export(string path, IEnumerable<Workflow> workflows)
    {
        var items = workflows.ToArray();
        foreach (var item in items) WorkflowRules.Validate(item);
        AtomicFile.Write(path, JsonSerializer.Serialize(new WorkflowArchive("MetadataTransferWorkflows", 1, items), Json));
    }
    public void Save(IEnumerable<Workflow> workflows) => Export(Path, workflows);
    public static Workflow[] Merge(IEnumerable<Workflow> current, IEnumerable<Workflow> incoming)
    {
        var output = current.ToList();
        foreach (var item in incoming)
        {
            WorkflowRules.Validate(item);
            if (!output.Any(w => w.Name == item.Name && w.Category == item.Category && w.Operations.SequenceEqual(item.Operations)))
                output.Add(item with { Id = Guid.NewGuid().ToString() });
        }
        return output.ToArray();
    }
}
public static class AtomicFile
{
    public static void Write(string path, string text)
    {
        var full = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text); File.Move(temp, full, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public sealed class Settings
{
    public List<FavouriteLocation> MapFavourites { get; set; } = [];
    public string MapStyle { get; set; } = "Normal";
    public bool MapShowsSelectedOnly { get; set; }
    public string? DefaultExports { get; set; }
    public string? ExifTool { get; set; }
    public bool ShowInspector { get; set; } = true;
    public double MediaPaneWidth { get; set; } = 440;
    public Dictionary<string, List<string>> MediaColumns { get; set; } = [];
    public double Width { get; set; } = 1360;
    public double Height { get; set; } = 860;
    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Metadata Transfer");
    public static string FilePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Metadata Transfer", "Settings.json");
    public static Settings Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), WorkflowStore.Json) ?? new() : new();
    public void Save() => AtomicFile.Write(FilePath, JsonSerializer.Serialize(this, WorkflowStore.Json));
}
