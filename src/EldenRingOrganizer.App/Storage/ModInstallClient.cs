using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace EldenRingOrganizer.Storage;

public sealed class ModInstallClient
{
    private readonly string _modsRoot;
    private readonly string _cacheRoot;
    private readonly string _logsRoot;

    public ModInstallClient(string modsRoot, string cacheRoot, string logsRoot)
    {
        _modsRoot = modsRoot;
        _cacheRoot = cacheRoot;
        _logsRoot = logsRoot;
    }

    public async Task<InstalledMod> InstallArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("The selected mod archive does not exist.", archivePath);
        }

        Directory.CreateDirectory(_logsRoot);
        var helperLog = Path.Combine(_logsRoot, "installer-helper.log");

        using var process = new Process
        {
            StartInfo = CreateStartInfo(archivePath, helperLog)
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the isolated mod installer.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr)
                ? $"Installer helper exited with code {process.ExitCode}."
                : GetLastUsefulLine(stderr);

            throw new InvalidDataException($"{detail} Details: {helperLog}");
        }

        var result = JsonSerializer.Deserialize<ModInstallHelperResult>(
            stdout.Trim(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result is null ||
            string.IsNullOrWhiteSpace(result.Name) ||
            string.IsNullOrWhiteSpace(result.RootPath))
        {
            throw new InvalidDataException(
                $"Installer helper completed without a valid result. Details: {helperLog}");
        }

        return new InstalledMod
        {
            Name = result.Name,
            RootPath = result.RootPath,
            Enabled = result.Enabled,
            InstalledFrom = result.InstalledFrom,
            InstalledAtUtc = result.InstalledAtUtc
        };
    }

    private ProcessStartInfo CreateStartInfo(string archivePath, string logPath)
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

        info.ArgumentList.Add(ModInstallCommand.CommandName);
        info.ArgumentList.Add(archivePath);
        info.ArgumentList.Add(_modsRoot);
        info.ArgumentList.Add(_cacheRoot);
        info.ArgumentList.Add(logPath);

        return info;
    }

    private static string GetLastUsefulLine(string text)
    {
        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .LastOrDefault(x => x.Length > 0)
            ?? "Installer helper failed.";
    }

    private sealed class ModInstallHelperResult
    {
        public string Name { get; init; } = "";
        public string RootPath { get; init; } = "";
        public bool Enabled { get; init; }
        public string? InstalledFrom { get; init; }
        public DateTime InstalledAtUtc { get; init; }
    }
}
