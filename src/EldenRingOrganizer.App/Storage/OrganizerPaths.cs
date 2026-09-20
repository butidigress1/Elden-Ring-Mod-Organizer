namespace EldenRingOrganizer.Storage;

public sealed class OrganizerPaths
{
    public OrganizerPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Mods = Path.Combine(Root, "mods");
        Profiles = Path.Combine(Root, "profiles");
        Cache = Path.Combine(Root, "cache");
        Generated = Path.Combine(Root, "generated");
        Logs = Path.Combine(Root, "logs");
        Settings = Path.Combine(Root, "settings.json");
    }

    public string Root { get; }
    public string Mods { get; }
    public string Profiles { get; }
    public string Cache { get; }
    public string Generated { get; }
    public string Logs { get; }
    public string Settings { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Mods);
        Directory.CreateDirectory(Profiles);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Generated);
        Directory.CreateDirectory(Logs);
    }
}
