using SharpCompress.Archives;
using SharpCompress.Common;
using System.Text.Json;

namespace EldenRingOrganizer.Storage;

public sealed class ModCatalog
{
    private const string ManifestName = "ero.mod.json";
    private const string StagingPrefix = ".installing-";
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
        CleanupAbandonedStaging();

        return Directory.EnumerateDirectories(_modsRoot)
            .Where(path => !Path.GetFileName(path).StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(ReadMod)
            .Where(x => x is not null)
            .Cast<InstalledMod>()
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public InstalledMod InstallArchive(string archivePath, Action<string>? trace = null)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("The selected mod archive does not exist.", archivePath);
        }

        Directory.CreateDirectory(_modsRoot);
        trace?.Invoke("VALIDATED archive and mods root");

        trace?.Invoke("PREFLIGHT begin");
        var archiveLayout = AnalyzeArchive(archivePath);
        trace?.Invoke(
            $"PREFLIGHT {archiveLayout.FileCount} files, {archiveLayout.TotalUncompressedBytes} bytes, root '{archiveLayout.RootPrefix ?? "<auto>"}'");

        var installId = Guid.NewGuid().ToString("N");
        var tempRoot = Path.Combine(_cacheRoot, "install", installId);
        var extractRoot = Path.Combine(tempRoot, "extract");
        var stageRoot = Path.Combine(_modsRoot, $"{StagingPrefix}{installId}");

        Directory.CreateDirectory(extractRoot);

        try
        {
            trace?.Invoke("EXTRACT begin");
            ExtractArchive(archivePath, extractRoot);
            trace?.Invoke("EXTRACT complete");

            trace?.Invoke("ROOT-DETECT begin");
            var sourceRoot = archiveLayout.RootPrefix is null
                ? FindProjectRoot(extractRoot)
                : Path.Combine(
                    extractRoot,
                    archiveLayout.RootPrefix.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(sourceRoot))
            {
                throw new InvalidDataException(
                    "The archive preflight root was not present after extraction.");
            }

            trace?.Invoke($"ROOT-DETECT {sourceRoot}");
            var name = SanitizeName(Path.GetFileNameWithoutExtension(archivePath));
            var targetRoot = GetUniqueTarget(name);

            trace?.Invoke($"STAGE begin {stageRoot}");
            CopyDirectory(sourceRoot, stageRoot);
            trace?.Invoke("STAGE files copied");

            var manifest = new ModManifest
            {
                Name = Path.GetFileName(targetRoot),
                Enabled = true,
                InstalledFrom = Path.GetFileName(archivePath),
                InstalledAtUtc = DateTime.UtcNow
            };

            SaveManifest(stageRoot, manifest);
            trace?.Invoke("STAGE manifest written");

            trace?.Invoke($"COMMIT begin {targetRoot}");
            Directory.Move(stageRoot, targetRoot);
            trace?.Invoke("COMMIT complete");
            return ToInstalledMod(targetRoot, manifest);
        }
        catch (Exception ex)
        {
            trace?.Invoke($"ROLLBACK {ex.GetType().Name}: {ex.Message}");
            DeleteDirectoryBestEffort(stageRoot);
            throw;
        }
        finally
        {
            trace?.Invoke("CLEANUP temp");
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    public InstalledMod InstallZip(string archivePath)
    {
        return InstallArchive(archivePath, null);
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

    private static ArchiveLayout AnalyzeArchive(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var paths = new List<string>();
            long totalBytes = 0;

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }

                var normalized = NormalizeArchivePath(
                    entry.Key ?? throw new InvalidDataException("The archive contains a file entry without a path."));
                paths.Add(normalized);

                if (entry.Size > 0)
                {
                    totalBytes = checked(totalBytes + entry.Size);
                }
            }

            if (paths.Count == 0)
            {
                throw new InvalidDataException("The selected archive contains no files.");
            }

            var rootPrefix = DetectArchiveRootPrefix(paths);
            return new ArchiveLayout(rootPrefix, paths.Count, totalBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidDataException)
        {
            throw new InvalidDataException(
                $"Could not inspect '{Path.GetFileName(archivePath)}'. The archive may be damaged, encrypted, incomplete, or unsupported.",
                ex);
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException("The archive contains an empty file path.");
        }

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"The archive contains an unsafe path: {path}");
        }

        if (Path.IsPathRooted(path) ||
            normalized.Contains(':'))
        {
            throw new InvalidDataException(
                $"The archive contains an absolute or unsafe path: {path}");
        }

        return string.Join('/', segments);
    }

    private static string? DetectArchiveRootPrefix(IReadOnlyList<string> paths)
    {
        var candidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var segments = path.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                if (!IsGameDataMarker(segments[index], index == segments.Length - 1))
                {
                    continue;
                }

                var prefix = index == 0
                    ? ""
                    : string.Join('/', segments.Take(index));

                candidates.TryGetValue(prefix, out var score);
                candidates[prefix] = score + MarkerScore(segments[index]);
                break;
            }
        }

        if (candidates.Count == 0)
        {
            return DetectSingleWrapperPrefix(paths);
        }

        var bestScore = candidates.Values.Max();
        var best = candidates
            .Where(pair => pair.Value == bestScore)
            .Select(pair => pair.Key)
            .OrderBy(prefix => prefix.Count(c => c == '/'))
            .ThenBy(prefix => prefix.Length)
            .ToArray();

        if (best.Length > 1)
        {
            var modRoots = best
                .Where(prefix => string.Equals(
                    prefix.Split('/').LastOrDefault(),
                    "mod",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (modRoots.Length == 1)
            {
                return modRoots[0];
            }

            throw new InvalidDataException(
                "The archive contains multiple equally plausible Elden Ring data roots. ERO will not guess which one to install.");
        }

        return string.IsNullOrEmpty(best[0]) ? null : best[0];
    }

    private static string? DetectSingleWrapperPrefix(IReadOnlyList<string> paths)
    {
        var firstSegments = paths
            .Select(path => path.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (firstSegments.Length != 1)
        {
            return null;
        }

        var wrapper = firstSegments[0];

        return paths.All(path => path.Contains('/'))
            ? wrapper
            : null;
    }

    private static bool IsGameDataMarker(string segment, bool isFile)
    {
        if (isFile && string.Equals(segment, "regulation.bin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isFile)
        {
            return false;
        }

        return segment.Equals("msg", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("parts", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("chr", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("map", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("menu", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("asset", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("sfx", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("event", StringComparison.OrdinalIgnoreCase);
    }

    private static int MarkerScore(string marker)
    {
        return string.Equals(marker, "regulation.bin", StringComparison.OrdinalIgnoreCase)
            ? 8
            : 1;
    }

    private static void ExtractArchive(string archivePath, string destination)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            archive.WriteToDirectory(
                destination,
                new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = false,
                    CheckCrc = true
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                $"Could not extract '{Path.GetFileName(archivePath)}'. The archive may be damaged, encrypted, incomplete, or unsupported.",
                ex);
        }
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
        Directory.CreateDirectory(root);
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
        var candidates = new List<(string Path, int Depth, int Score)>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((extractedRoot, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (LooksLikeGameDataRoot(current))
            {
                candidates.Add((current, depth, ScoreGameDataRoot(current)));
            }

            if (depth >= 4)
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, ".smithbox", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                queue.Enqueue((directory, depth + 1));
            }
        }

        if (candidates.Count == 0)
        {
            return CollapseSingleWrapperDirectories(extractedRoot);
        }

        var bestDepth = candidates.Min(x => x.Depth);
        var atBestDepth = candidates.Where(x => x.Depth == bestDepth).ToArray();
        var bestScore = atBestDepth.Max(x => x.Score);
        var best = atBestDepth.Where(x => x.Score == bestScore).ToArray();

        if (best.Length > 1)
        {
            var modNamed = best.Where(x =>
                string.Equals(Path.GetFileName(x.Path), "mod", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (modNamed.Length == 1)
            {
                return modNamed[0].Path;
            }

            throw new InvalidDataException(
                "The archive contains multiple possible Elden Ring mod roots. ERO will not guess which one to install.");
        }

        return best[0].Path;
    }

    private static string CollapseSingleWrapperDirectories(string root)
    {
        var current = root;

        while (true)
        {
            var files = Directory.EnumerateFiles(current).ToArray();
            var directories = Directory.EnumerateDirectories(current).ToArray();

            if (files.Length != 0 || directories.Length != 1)
            {
                return current;
            }

            current = directories[0];
        }
    }

    private static bool LooksLikeGameDataRoot(string root)
    {
        return File.Exists(Path.Combine(root, "regulation.bin")) ||
               Directory.Exists(Path.Combine(root, "msg")) ||
               Directory.Exists(Path.Combine(root, "parts")) ||
               Directory.Exists(Path.Combine(root, "chr")) ||
               Directory.Exists(Path.Combine(root, "map")) ||
               Directory.Exists(Path.Combine(root, "menu")) ||
               Directory.Exists(Path.Combine(root, "asset")) ||
               Directory.Exists(Path.Combine(root, "sfx")) ||
               Directory.Exists(Path.Combine(root, "event"));
    }

    private static int ScoreGameDataRoot(string root)
    {
        var score = 0;

        if (File.Exists(Path.Combine(root, "regulation.bin")))
        {
            score += 8;
        }

        foreach (var folder in new[] { "msg", "parts", "chr", "map", "menu", "asset", "sfx", "event" })
        {
            if (Directory.Exists(Path.Combine(root, folder)))
            {
                score++;
            }
        }

        return score;
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

    private void CleanupAbandonedStaging()
    {
        foreach (var path in Directory.EnumerateDirectories(_modsRoot, $"{StagingPrefix}*"))
        {
            DeleteDirectoryBestEffort(path);
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Installed Mod" : sanitized;
    }

    private sealed record ArchiveLayout(
        string? RootPrefix,
        int FileCount,
        long TotalUncompressedBytes);

    private sealed class ModManifest
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string? InstalledFrom { get; set; }
        public DateTime InstalledAtUtc { get; set; }
    }
}
