using System.IO.Compression;
using System.Text.Json;

namespace EldenRingOrganizer.Storage;

public sealed class ModCatalog
{
    private const string ManifestName = "ero.mod.json";
    private readonly string _modsRoot;
    private readonly string _cacheRoot;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ModCatalog(string modsRoot, string cacheRoot)
    {
        _modsRoot = modsRoot;
        _cacheRoot = cacheRoot;
    }

    public IReadOnlyList<InstalledMod> Refresh()
    {
        Directory.CreateDirectory(_modsRoot);

        return Directory.EnumerateDirectories(_modsRoot)
            .Select(ReadMod)
            .Where(x => x is not null)
            .Cast<InstalledMod>()
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public InstalledMod InstallZip(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("The selected mod archive does not exist.", archivePath);
        }

        var tempRoot = Path.Combine(_cacheRoot, "install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(archivePath, tempRoot);
            var sourceRoot = FindProjectRoot(tempRoot);
            var name = SanitizeName(Path.GetFileNameWithoutExtension(archivePath));
            var targetRoot = GetUniqueTarget(name);

            CopyDirectory(sourceRoot, targetRoot);

            var manifest = new ModManifest
            {
                Name = Path.GetFileName(targetRoot),
                Enabled = true,
                InstalledFrom = Path.GetFileName(archivePath),
                InstalledAtUtc = DateTime.UtcNow
            };

            SaveManifest(targetRoot, manifest);
            return ToInstalledMod(targetRoot, manifest);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    public void SetEnabled(InstalledMod mod, bool enabled)
    {
        var manifest = ReadManifest(mod.RootPath) ?? new ModManifest
        {
            Name = mod.Name,
            Enabled = mod.Enabled,
            InstalledFrom = mod.InstalledFrom,
            InstalledAtUtc = mod.InstalledAtUtc
        };

        manifest.Enabled = enabled;
        SaveManifest(mod.RootPath, manifest);
        mod.Enabled = enabled;
    }

    private InstalledMod? ReadMod(string root)
    {
        try
        {
            var manifest = ReadManifest(root) ?? new ModManifest
            {
                Name = Path.GetFileName(root),
                Enabled = true,
                InstalledAtUtc = Directory.GetCreationTimeUtc(root)
            };

            return ToInstalledMod(root, manifest);
        }
        catch
        {
            return null;
        }
    }

    private InstalledMod ToInstalledMod(string root, ModManifest manifest)
    {
        return new InstalledMod
        {
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(root) : manifest.Name,
            RootPath = root,
            Enabled = manifest.Enabled,
            InstalledFrom = manifest.InstalledFrom,
            InstalledAtUtc = manifest.InstalledAtUtc
        };
    }

    private ModManifest? ReadManifest(string root)
    {
        var path = Path.Combine(root, ManifestName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(path), _jsonOptions);
    }

    private void SaveManifest(string root, ModManifest manifest)
    {
        File.WriteAllText(
            Path.Combine(root, ManifestName),
            JsonSerializer.Serialize(manifest, _jsonOptions));
    }

    private string GetUniqueTarget(string name)
    {
        var candidate = Path.Combine(_modsRoot, name);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(_modsRoot, $"{name} ({i})");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string FindProjectRoot(string extractedRoot)
    {
        var current = extractedRoot;

        while (true)
        {
            var files = Directory.EnumerateFiles(current).ToArray();
            var directories = Directory.EnumerateDirectories(current).ToArray();

            if (files.Length == 0 && directories.Length == 1)
            {
                current = directories[0];
                continue;
            }

            var modDirectory = directories.FirstOrDefault(x =>
                string.Equals(Path.GetFileName(x), "mod", StringComparison.OrdinalIgnoreCase));

            if (modDirectory is not null && LooksLikeGameDataRoot(modDirectory))
            {
                return modDirectory;
            }

            return current;
        }
    }

    private static bool LooksLikeGameDataRoot(string root)
    {
        return File.Exists(Path.Combine(root, "regulation.bin")) ||
               Directory.Exists(Path.Combine(root, "msg")) ||
               Directory.Exists(Path.Combine(root, "parts")) ||
               Directory.Exists(Path.Combine(root, "chr")) ||
               Directory.Exists(Path.Combine(root, "map")) ||
               Directory.Exists(Path.Combine(root, "menu"));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Installed Mod" : sanitized;
    }

    private sealed class ModManifest
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string? InstalledFrom { get; set; }
        public DateTime InstalledAtUtc { get; set; }
    }
}
