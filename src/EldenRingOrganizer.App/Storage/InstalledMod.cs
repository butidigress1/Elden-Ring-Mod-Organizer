namespace EldenRingOrganizer.Storage;

public sealed class InstalledMod
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public bool Enabled { get; set; }
    public string? InstalledFrom { get; init; }
    public DateTime InstalledAtUtc { get; init; }
    public bool HasRegulation => File.Exists(Path.Combine(RootPath, "regulation.bin"));
    public bool HasText => Directory.Exists(Path.Combine(RootPath, "msg"));
}
