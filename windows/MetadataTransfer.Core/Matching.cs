namespace MetadataTransfer.Core;

public static class Matching
{
    private static readonly HashSet<string> Images = new("3fr arq arw avif bmp cr2 cr3 crw dcr dng erf fff gif gpr heic heif hif iiq insp j2k jp2 jpeg jpg jxl mef mos mrw nef nrw orf pef png psb psd raf raw rw2 rwl sr2 srf srw tif tiff webp x3f".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Videos = new("3g2 3gp 3gp2 3gpp avi crm dv f4v m2ts m4v mkv mov mp4 mpeg mpg mts mxf qt webm wmv".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static string? Kind(string path) => Images.Contains(Path.GetExtension(path).TrimStart('.')) ? "image" : Videos.Contains(Path.GetExtension(path).TrimStart('.')) ? "video" : null;
    public static IReadOnlyList<string> Expand(IEnumerable<string> inputs, CancellationToken cancel = default)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            cancel.ThrowIfCancellationRequested();
            if (Directory.Exists(input))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
                foreach (var path in Directory.EnumerateFiles(input, "*", options))
                { cancel.ThrowIfCancellationRequested(); if (Kind(path) is not null) paths.Add(Path.GetFullPath(path)); }
            }
            else if (File.Exists(input) && Kind(input) is not null) paths.Add(Path.GetFullPath(input));
        }
        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) && File.ResolveLinkTarget(full, true) is FileSystemInfo target) return target.FullName;
        return full;
    }
    private static string Key(string path) => Kind(path) + ":" + Path.GetFileNameWithoutExtension(path);
    public static Match[] Compare(IEnumerable<string> originals, IEnumerable<string> exports)
    {
        var sources = originals.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var paths = new HashSet<string>(sources.Select(CanonicalPath), StringComparer.OrdinalIgnoreCase);
        var index = sources.GroupBy(Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Match>();
        foreach (var export in exports.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (paths.Contains(CanonicalPath(export))) { rows.Add(new(null, export, "Same file · skipped")); continue; }
            var key = Key(export);
            index.TryGetValue(key, out var candidates);
            if ((candidates is null || candidates.Length == 0) && key.EndsWith("-HDR", StringComparison.OrdinalIgnoreCase)) index.TryGetValue(key[..^4], out candidates);
            var source = candidates?.Length == 1 ? candidates[0] : null;
            if (source is not null) matched.Add(source);
            rows.Add(new(source, export, source is not null ? "Matched" : candidates?.Length > 1 ? "Ambiguous · skipped" : "No match"));
        }
        rows.AddRange(sources.Where(s => !matched.Contains(s)).Select(s => new Match(s, null, "Original only")));
        return rows.ToArray();
    }
}
