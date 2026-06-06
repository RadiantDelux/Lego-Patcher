using System.IO.Compression;
using ToyPadMaui.Models;

namespace ToyPadMaui.Services;

// Stores imported .bin dumps in app data (never bundled). Resolves thumbnails
// from embedded Raw assets. The game .bin files are user-supplied.
public class FigureLibrary
{
    readonly string _binDir =
        Path.Combine(FileSystem.AppDataDirectory, "figures");

    public List<Figure> Figures { get; private set; } = [];

    public bool HasFigures => Figures.Count > 0;

    public FigureLibrary()
    {
        Directory.CreateDirectory(_binDir);
    }

    static FigureCategory ParseCat(string top) => top.ToLowerInvariant() switch
    {
        "characters" => FigureCategory.Character,
        "vehicles"   => FigureCategory.Vehicle,
        "gadgets"    => FigureCategory.Gadget,
        _ => FigureCategory.Character,
    };

    // Loads metadata for previously imported figures from app data.
    public async Task LoadAsync()
    {
        Figures.Clear();
        if (!Directory.Exists(_binDir)) return;

        foreach (var sub in new[] { "Characters", "Vehicles", "Gadgets" })
        {
            var dir = Path.Combine(_binDir, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.bin").OrderBy(x => x))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                Figures.Add(new Figure
                {
                    Name = name,
                    RelPath = $"{sub}/{name}.bin",
                    Category = ParseCat(sub),
                    ThumbFile = await ResolveThumbAsync(name),
                });
            }
        }
    }

    // Reads the actual bytes for a figure on demand (kept off the metadata list).
    public byte[] ReadBytes(Figure f)
    {
        var path = Path.Combine(_binDir, f.RelPath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllBytes(path);
    }

    // Imports a Dimensions zip: extracts Characters/Vehicles/Gadgets *.bin.
    public async Task<int> ImportZipAsync(Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToList();
        if (entries.Count == 0) throw new InvalidOperationException("zip vacío");

        // Strip a leading "Dimensions/" prefix if present.
        string prefix = "";
        var first = entries[0].FullName.Replace('\\', '/');
        var firstTop = first.Split('/')[0];
        if ((firstTop.Equals("Dimensions", StringComparison.OrdinalIgnoreCase))
            && entries.All(e => e.FullName.Replace('\\', '/').StartsWith(firstTop + "/")))
            prefix = firstTop + "/";

        var wanted = new[] { "Characters", "Vehicles", "Gadgets" };
        int count = 0;

        foreach (var e in entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (prefix.Length > 0 && rel.StartsWith(prefix)) rel = rel[prefix.Length..];
            var parts = rel.Split('/');
            if (parts.Length < 2) continue;
            var top = parts[0];
            if (!wanted.Contains(top, StringComparer.OrdinalIgnoreCase)) continue;
            if (!rel.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) continue;

            using var es = e.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length is < 64 or > 1024) continue;

            var name = Path.GetFileNameWithoutExtension(parts[^1]);
            var outDir = Path.Combine(_binDir, top);
            Directory.CreateDirectory(outDir);
            await File.WriteAllBytesAsync(Path.Combine(outDir, name + ".bin"), bytes);
            count++;
        }

        if (count == 0)
            throw new InvalidOperationException("zip sin Characters/Vehicles/Gadgets/*.bin");

        AppSettings.DimensionsImported = true;
        await LoadAsync();
        return count;
    }

    // Resolve embedded thumbnail (Raw/thumbs/<name>.<ext>) -> a cached file URI.
    static readonly string[] ThumbExt = [".png", ".jpg", ".jpeg", ".webp"];
    readonly Dictionary<string, string?> _thumbCache = [];

    async Task<string?> ResolveThumbAsync(string name)
    {
        if (_thumbCache.TryGetValue(name, out var cached)) return cached;

        string? found = null;
        foreach (var ext in ThumbExt)
        {
            var asset = $"thumbs/{name}{ext}";
            if (await FileSystem.AppPackageFileExistsAsync(asset))
            {
                // Copy to cache dir so Image can load via file path.
                var cacheDir = Path.Combine(FileSystem.CacheDirectory, "thumbs");
                Directory.CreateDirectory(cacheDir);
                var dst = Path.Combine(cacheDir, name + ext);
                if (!File.Exists(dst))
                {
                    using var src = await FileSystem.OpenAppPackageFileAsync(asset);
                    using var o = File.Create(dst);
                    await src.CopyToAsync(o);
                }
                found = dst;
                break;
            }
        }
        _thumbCache[name] = found;
        return found;
    }

    // Reads an embedded artifact (sprx / EBOOT) fully into memory.
    public static async Task<byte[]> ReadAssetAsync(string assetName)
    {
        using var s = await FileSystem.OpenAppPackageFileAsync(assetName);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }
}
