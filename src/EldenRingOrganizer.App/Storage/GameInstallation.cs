namespace EldenRingOrganizer.Storage;

public sealed record GameInstallation(
    string Folder,
    bool HasExecutable,
    bool HasRegulation,
    bool HasData0Header,
    bool HasData0Data)
{
    public bool IsValid => HasExecutable && HasRegulation;

    public static GameInstallation Inspect(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        return new GameInstallation(
            fullPath,
            File.Exists(Path.Combine(fullPath, "eldenring.exe")),
            File.Exists(Path.Combine(fullPath, "regulation.bin")),
            File.Exists(Path.Combine(fullPath, "Data0.bhd")),
            File.Exists(Path.Combine(fullPath, "Data0.bdt")));
    }
}
