using System.Text.Json;

namespace EldenRingOrganizer.Storage;

public static class ModInstallCommand
{
    public const string CommandName = "--install-mod";

    public static bool IsCommand(string[] args)
    {
        return args.Length > 0 &&
               string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    public static int Run(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine("Invalid installer helper arguments.");
            return 2;
        }

        var archivePath = args[1];
        var modsRoot = args[2];
        var cacheRoot = args[3];
        var logPath = args[4];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            void Trace(string message)
            {
                File.AppendAllText(
                    logPath,
                    $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
            }

            Trace($"BEGIN {Path.GetFileName(archivePath)}");

            var catalog = new ModCatalog(modsRoot, cacheRoot);
            var installed = catalog.InstallArchive(archivePath, Trace);

            Trace($"SUCCESS {installed.RootPath}");

            Console.Out.WriteLine(JsonSerializer.Serialize(new ModInstallHelperResult
            {
                Name = installed.Name,
                RootPath = installed.RootPath,
                Enabled = installed.Enabled,
                InstalledFrom = installed.InstalledFrom,
                InstalledAtUtc = installed.InstalledAtUtc
            }));

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    logPath,
                    $"[{DateTime.UtcNow:O}] FAILURE {ex}{Environment.NewLine}");
            }
            catch
            {
            }

            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private sealed class ModInstallHelperResult
    {
        public required string Name { get; init; }
        public required string RootPath { get; init; }
        public bool Enabled { get; init; }
        public string? InstalledFrom { get; init; }
        public DateTime InstalledAtUtc { get; init; }
    }
}
