using System.Text.RegularExpressions;
namespace MetadataTransfer.Core;

public sealed record MetadataField(string Tag, string Name, string Group, string Category, string Value, string? Reference, bool Mixed, bool Different, string? WriteTag)
{
    public bool Writable => WriteTag is not null;
    public string Display => Mixed ? "Multiple values" : Value.Length == 0 ? "Not set" : Value;
}
public static partial class Metadata
{
    public const string Missing = "";
    private static string Name(string key) => key.Split(':').Last();
    private static string Group(string key) => key.Contains(':') ? key.Split(':')[0] : "General";
    private static bool Visible(string key) => key is "System:FileSize" or "System:FileCreateDate" or "System:FileModifyDate" ||
        key != "SourceFile" && !key.StartsWith("System:") && !key.StartsWith("ExifTool:");
    private static string Canonical(string key) => Name(key) is "Make" or "Model" ? Name(key) : key;
    public static string Readable(string name) => Regex.Replace(Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1 $2"), "([a-z0-9])([A-Z])", "$1 $2");
    public static string Category(string key)
    {
        var n = Name(key).ToLowerInvariant(); var g = Group(key).ToLowerInvariant();
        if (g.Contains("gps") || n.Contains("gps") || n.Contains("latitude") || n.Contains("longitude") || n.Contains("location") || new[]{"city","country","countrycode","state","sublocation"}.Contains(n)) return "Location";
        if (n.Contains("date") || n.Contains("time") && !n.Contains("duration") && !n.Contains("scale") && n != "exposuretime") return "Dates & Times";
        if (n.Contains("lens") || new[]{"make","model","serialnumber","cameramodelname","software"}.Contains(n)) return "Camera & Lens";
        if (new[]{"exposure","aperture","shutter","focal","iso","flash","whitebalance","fnumber"}.Any(n.Contains)) return "Capture Settings";
        if (new[]{"audio","sample","channel"}.Any(n.Contains)) return "Audio";
        if (new[]{"color","colour","profile","gamma","primaries"}.Any(n.Contains)) return "Colour";
        if (new[]{"video","frame","compressor","bitrate"}.Any(n.Contains)) return "Video";
        if (new[]{"filetype","filesize","mimetype","imagesize","imagewidth","imageheight","duration","rotation","megapixels"}.Contains(n)) return "Information";
        return "Other Metadata";
    }
    public static readonly string[] Categories = ["Information", "Camera & Lens", "Capture Settings", "Dates & Times", "Location", "Video", "Audio", "Colour", "Other Metadata"];
    private static readonly string[] Common = ["Make","Model","LensMake","LensModel","LensInfo","Software","FNumber","ExposureTime","ISO","FocalLength","DateTimeOriginal","CreateDate","ModifyDate","Artist","Copyright","Title","Description","Keywords","GPSLatitude","GPSLongitude","GPSAltitude"];
    private static HashSet<string> Values(Dictionary<string,string> doc, string key)
    {
        if (key is "Make" or "Model") return doc.Where(x => Name(x.Key) == key).Select(x=>x.Value).ToHashSet();
        return doc.TryGetValue(key, out var value) ? [value] : [];
    }
    public static MetadataField[] Build(IReadOnlyList<Dictionary<string,string>> targets, IReadOnlyList<Dictionary<string,string>> references, bool includeEmpty = true)
    {
        var keys = targets.Concat(references).SelectMany(x => x.Keys).Where(Visible).Select(Canonical).ToHashSet();
        if (includeEmpty)
            foreach (var name in Common)
                if (!keys.Any(k => Name(k) == name && Group(k) != "Composite")) keys.Add(name);
        var fields = new List<MetadataField>();
        foreach (var key in keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            var values = targets.SelectMany(d => { var v = Values(d,key); return v.Count > 0 ? v : new HashSet<string>{Missing}; }).ToHashSet();
            var refs = references.SelectMany(d => Values(d,key)).ToHashSet();
            var mixed = values.Count > 1;
            var value = values.FirstOrDefault() ?? Missing;
            string? reference = refs.Count == 0 ? null : refs.Count > 1 ? "Multiple values" : refs.First();
            var group = Group(key); var name = Name(key);
            string? write = new[]{"File","System","ExifTool","Composite"}.Contains(group) ? null : key;
            if (group == "Composite")
            {
                var underlying = name switch { "LensID" => "LensModel", "Aperture" => "FNumber", "ShutterSpeed" => "ExposureTime", _ => null };
                if (underlying is not null) write = keys.FirstOrDefault(k => Name(k) == underlying && Group(k) != "Composite") ?? underlying;
            }
            fields.Add(new(key, name, group, Category(key), mixed ? "" : value, reference, mixed, mixed || (reference ?? "") != value, write));
        }
        return fields.ToArray();
    }
}
