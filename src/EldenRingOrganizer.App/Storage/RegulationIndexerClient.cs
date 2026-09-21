using EldenRingOrganizer.Semantic;
using System.Diagnostics;
using System.Reflection;

namespace EldenRingOrganizer.Storage;

public sealed record RegulationIndexResult(RegulationIndex Index, bool FromCache);

public sealed class RegulationIndexerClient
{
    private readonly RegulationCacheStore _cacheStore;

    public RegulationIndexerClient(RegulationCacheStore cacheStore)
    {
        _cacheStore = cacheStore;
    }

    public async Task<RegulationIndexResult> GetOrBuildAsync(
        InstalledMod mod,
        string gameFolder,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var vanillaRegulation = Path.Combine(gameFolder, "regulation.bin");
        var cached = force ? null : _cacheStore.TryLoadCurrent(mod, vanillaRegulation);
        if (cached is not null)
        {
            return new RegulationIndexResult(cached, true);
        }

        var modRegulation = Path.Combine(mod.RootPath, "regulation.bin");
        if (!File.Exists(modRegulation))
        {
            throw new FileNotFoundException("The installed mod does not contain regulation.bin.", modRegulation);
        }

        if (!File.Exists(vanillaRegulation))
        {
            throw new FileNotFoundException("The configured game folder does not contain regulation.bin.", vanillaRegulation);
        }

        if (force)
        {
            _cacheStore.Delete(mod);
        }

        var output = _cacheStore.GetCachePath(mod);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(gameFolder, mod.RootPath, output, mod.Name)
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the isolated regulation indexer.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(stderr)
                ? string.IsNullOrWhiteSpace(stdout)
                    ? $"Regulation indexer exited with code {process.ExitCode}."
                    : stdout.Trim()
                : stderr.Trim();

            throw new InvalidDataException(message);
        }

        if (!File.Exists(output))
        {
            throw new InvalidDataException("Regulation indexer completed without producing a semantic cache.");
        }

        return new RegulationIndexResult(_cacheStore.Read(output), false);
    }

    private static ProcessStartInfo CreateStartInfo(
        string gameFolder,
        string modRoot,
        string output,
        string sourceName)
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Could not determine the ERO executable path.");

        var info = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        info.ArgumentList.Add("--index-regulation");
        info.ArgumentList.Add(gameFolder);
        info.ArgumentList.Add(modRoot);
        info.ArgumentList.Add(output);
        info.ArgumentList.Add(sourceName);

        return info;
    }
}
