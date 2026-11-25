using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MetadataTransfer.Core;

public sealed class ExifService
{
    public string Executable { get; }
    private readonly SemaphoreSlim slots = new(4);
    public ExifService(string executable) { Executable = executable; }
    public static string? Find(string? configured)
    {
        var choices = new List<string?> { configured, Path.Combine(AppContext.BaseDirectory,"tools","exiftool.exe"), Path.Combine(AppContext.BaseDirectory,"exiftool.exe") };
        choices.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p=>Path.Combine(p,OperatingSystem.IsWindows()?"exiftool.exe":"exiftool")));
        return choices.FirstOrDefault(p=> !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }
    public async Task<(int ExitCode,string Output)> RunAsync(IEnumerable<string> args, CancellationToken cancel = default)
    {
        var (code, output, error) = await RunProcessAsync(args, cancel);
        return (code, output + (error.Length > 0 ? "\n" + error : ""));
    }
    private async Task<(int ExitCode,string Output,string Error)> RunProcessAsync(IEnumerable<string> args, CancellationToken cancel)
    {
        await slots.WaitAsync(cancel);
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var registration = cancel.Register(()=> { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) {} });
            await process.WaitForExitAsync();
            var output = await stdout; var error = await stderr;
            cancel.ThrowIfCancellationRequested();
            return (process.ExitCode, output, error);
        }
        finally { slots.Release(); }
    }
    public async Task<Dictionary<string,string>> ReadAsync(string path, CancellationToken cancel = default)
    {
        var (code, output, error) = await RunProcessAsync(["-j","-G1","-s",Path.GetFullPath(path)],cancel);
        if (code != 0) throw new IOException(Path.GetFileName(path) + ": " + output + error);
        using var doc = JsonDocument.Parse(output);
        return doc.RootElement[0].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString());
    }

    public async Task<Dictionary<string, (Dictionary<string,string> Tags, GeoPoint? Location)>> ReadWorkspaceBatchAsync(
        IReadOnlyList<string> paths, CancellationToken cancel)
    {
        // An argument file avoids Windows' command-line length limit for long Unicode paths.
        var argumentFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(argumentFile, paths.Select(Path.GetFullPath), cancel);
            var (_, output, error) = await RunProcessAsync(
                ["-charset", "filename=UTF8", "-j", "-G1", "-s", "-c", "%+.8f", "-@", argumentFile], cancel);
            if (string.IsNullOrWhiteSpace(output)) throw new IOException(error);
            using var document = JsonDocument.Parse(output);
            var result = new Dictionary<string, (Dictionary<string,string>, GeoPoint?)>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("SourceFile", out var source) || source.GetString() is not string file ||
                    item.TryGetProperty("ExifTool:Error", out _) || item.TryGetProperty("Error", out _)) continue;
                var path = Path.GetFullPath(file);
                var tags = item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
                result[path] = (tags, WorkspaceLocation(path, tags));
            }
            return result;
        }
        finally { File.Delete(argumentFile); }
    }

    private static GeoPoint? WorkspaceLocation(string path, Dictionary<string,string> tags)
    {
        static bool Number(string? value, out double number) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        static double Signed(double number, string? reference, char negative) =>
            reference?.StartsWith(negative.ToString(), StringComparison.OrdinalIgnoreCase) == true ? -Math.Abs(number) : number;

        if (!Number(tags.GetValueOrDefault("Composite:GPSLatitude"), out var latitude) ||
            !Number(tags.GetValueOrDefault("Composite:GPSLongitude"), out var longitude))
        {
            if (!Number(tags.GetValueOrDefault("GPS:GPSLatitude"), out latitude) ||
                !Number(tags.GetValueOrDefault("GPS:GPSLongitude"), out longitude))
                return null;
            latitude = Signed(latitude, tags.GetValueOrDefault("GPS:GPSLatitudeRef"), 'S');
            longitude = Signed(longitude, tags.GetValueOrDefault("GPS:GPSLongitudeRef"), 'W');
        }
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 ? new(path, latitude, longitude) : null;
    }
    public async Task<GeoPoint?> LocationAsync(string path, CancellationToken cancel = default)
    {
        var (code, output, _) = await RunProcessAsync(["-j","-n","-GPSLatitude","-GPSLongitude",Path.GetFullPath(path)],cancel);
        if(code != 0) return null;
        using var doc = JsonDocument.Parse(output);
        var item = doc.RootElement[0];
        if(item.TryGetProperty("GPSLatitude",out var lat) && item.TryGetProperty("GPSLongitude",out var lon) &&
           double.TryParse(lat.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var latitude) &&
           double.TryParse(lon.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var longitude) &&
           latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
            return new(path,latitude,longitude);
        return null;
    }
    public async Task<OperationResult> WriteLocationAsync(string path, double latitude, double longitude, IEnumerable<string>? alongside = null)
    {
        if (!(latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180))
            throw new ArgumentOutOfRangeException(nameof(latitude), "Coordinates are outside the valid geographic range.");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        string[] operations = extension is ".mp4" or ".mov" or ".m4v" or ".3gp" or ".3g2" or ".qt"
            ? [FormattableString.Invariant($"-Keys:GPSCoordinates#={latitude:F7} {longitude:F7}")]
            : [
                FormattableString.Invariant($"-EXIF:GPSLatitude#={Math.Abs(latitude):F7}"),
                $"-EXIF:GPSLatitudeRef={(latitude < 0 ? "S" : "N")}",
                FormattableString.Invariant($"-EXIF:GPSLongitude#={Math.Abs(longitude):F7}"),
                $"-EXIF:GPSLongitudeRef={(longitude < 0 ? "W" : "E")}"
            ];

        var result = await WriteAsync(path, alongside is null ? operations : alongside.Concat(operations));
        if (!result.Success) return result;
        var point = await LocationAsync(path);
        var verified = point is not null &&
            Math.Abs(point.Latitude - latitude) < 0.000001 && Math.Abs(point.Longitude - longitude) < 0.000001;
        return result with
        {
            Success = verified,
            Output = result.Output + (verified ? "\nGPS coordinates verified." : "\nCould not verify the GPS coordinates after writing.")
        };
    }
    public async Task<OperationResult> WriteAsync(string path, IEnumerable<string> operations)
    {
        var args = operations.ToList();
        if(args.Count == 0 || args.Any(x=>!WorkflowRules.Valid(x))) throw new InvalidDataException("Only metadata assignments and tag-copy operations are allowed.");
        var expected = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var op in args)
        {
            var index = op.IndexOf('='); if(index < 0) continue;
            var tag = op[1..index]; if(tag.Split(':').Last().ToLowerInvariant() is "make" or "model") expected[tag] = op[(index+1)..];
        }
        if(expected.Count > 0)
        {
            var existing = await ReadAsync(path);
            foreach(var (tag,value) in expected.Where(x=>!x.Key.Contains(':')))
            {
                if(new[]{".mp4",".mov",".m4v",".3gp",".3g2",".qt"}.Contains(Path.GetExtension(path).ToLowerInvariant())) args.Add($"-Keys:{tag}={value}");
                foreach(var key in existing.Keys.Where(k=> k.Contains(':') && k.Split(':').Last().Equals(tag,StringComparison.OrdinalIgnoreCase) && new[]{"IFD0","XMP-tiff","Keys","UserData","ItemList"}.Contains(k.Split(':')[0]))) args.Add($"-{key}={value}");
            }
        }
        var (code, output) = await RunAsync(new[]{"-P"}.Concat(args).Append(Path.GetFullPath(path)));
        var success = code == 0 && !output.Contains("Warning:") && !output.Contains("Error:");
        if(success && expected.Count > 0)
        {
            var readback = await ReadAsync(path);
            foreach(var (tag,value) in expected)
            {
                var actual = readback.Where(x=>tag.Contains(':') ? x.Key.Equals(tag,StringComparison.OrdinalIgnoreCase) : x.Key.Split(':').Last().Equals(tag,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Value).ToArray();
                var correct = value.Length == 0 ? actual.Length == 0 : actual.Length > 0 && actual.All(v=>v == value);
                success &= correct; output += $"\n{tag}: {(correct ? "verified" : "VERIFICATION FAILED")}";
            }
        }
        return new(path, success, output);
    }
    public async Task<OperationResult> CopyAsync(Match pair)
    {
        if(!pair.Matched || string.Equals(Matching.CanonicalPath(pair.Original!),Matching.CanonicalPath(pair.Export!),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This pair cannot be copied safely.");
        var (code,output) = await RunAsync(["-P","-TagsFromFile",Path.GetFullPath(pair.Original!),"-all:all",Path.GetFullPath(pair.Export!)]);
        return new(pair.Export!,code == 0 && !output.Contains("Warning:") && !output.Contains("Error:"),output);
    }
}
