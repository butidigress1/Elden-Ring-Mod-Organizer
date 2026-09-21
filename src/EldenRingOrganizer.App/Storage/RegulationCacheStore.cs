using EldenRingOrganizer.Semantic;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EldenRingOrganizer.Storage;

public sealed class RegulationCacheStore
{
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public RegulationCacheStore(string cacheRoot)
    {
        _root = Path.Combine(cacheRoot, "regulation");
    }

    public string GetCachePath(InstalledMod mod)
    {
        var normalized = Path.GetFullPath(mod.RootPath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        var safeName = string.Concat(mod.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_root, $"{safeName}-{hash}", "index.json.gz");
    }

    public RegulationIndex? TryLoadCurrent(InstalledMod mod, string vanillaRegulationPath)
    {
        var modRegulation = Path.Combine(mod.RootPath, "regulation.bin");
        if (!File.Exists(modRegulation) || !File.Exists(vanillaRegulationPath))
        {
            return null;
        }

        var cachePath = GetCachePath(mod);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            var index = Read(cachePath);
            if (index.SchemaVersion != RegulationIndex.CurrentSchemaVersion ||
                index.Document.SchemaVersion != RegulationDocument.CurrentSchemaVersion ||
                !string.Equals(
                    index.ParserRevision,
                    RegulationIndex.CurrentParserRevision,
                    StringComparison.Ordinal))
            {
                return null;
            }

            var modFingerprint = FileFingerprint.Create(modRegulation);
            var vanillaFingerprint = FileFingerprint.Create(vanillaRegulationPath);

            return index.Delta.ModSource.SameContent(modFingerprint) &&
                   index.Delta.VanillaSource.SameContent(vanillaFingerprint)
                ? index
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Delete(InstalledMod mod)
    {
        var path = GetCachePath(mod);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public RegulationIndex Read(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<RegulationIndex>(gzip, _jsonOptions)
               ?? throw new InvalidDataException("Regulation semantic cache is empty or invalid.");
    }

    public void WriteAtomic(string path, RegulationIndex index)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Cache path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, index, _jsonOptions);
            }

            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                }
            }
        }
    }
}
