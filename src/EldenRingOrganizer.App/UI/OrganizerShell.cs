using EldenRingOrganizer.Configuration;
using EldenRingOrganizer.Storage;
using Hexa.NET.ImGui;
using System.Numerics;
using System.Windows.Forms;

namespace EldenRingOrganizer.UI;

public sealed class OrganizerShell
{
    private readonly OrganizerPaths _paths;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private GameInstallation? _installation;
    private string _status = "Ready";
    private bool _statusIsError;

    public OrganizerShell(string root)
    {
        _paths = new OrganizerPaths(root);
        _paths.EnsureCreated();
        _settingsStore = new SettingsStore(_paths.Settings);
        _settings = _settingsStore.Load();

        if (!string.IsNullOrWhiteSpace(_settings.GameFolder) && Directory.Exists(_settings.GameFolder))
        {
            _installation = GameInstallation.Inspect(_settings.GameFolder);
            if (!_installation.IsValid)
            {
                _status = "The saved Game folder no longer looks like an Elden Ring installation.";
                _statusIsError = true;
            }
        }
    }

    public void ApplyStyle()
    {
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.WindowRounding = 0;
        style.ChildRounding = 3;
        style.FrameRounding = 3;
        style.PopupRounding = 3;
        style.TabRounding = 3;
        style.ScrollbarRounding = 4;
        style.FramePadding = new Vector2(7, 5);
        style.ItemSpacing = new Vector2(7, 5);
        style.CellPadding = new Vector2(7, 4);

        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.075f, 0.078f, 0.086f, 1f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.095f, 0.098f, 0.107f, 1f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.20f, 0.21f, 0.23f, 1f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.24f, 0.31f, 1f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.25f, 0.30f, 0.39f, 1f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.30f, 0.36f, 0.46f, 1f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.18f, 0.21f, 0.27f, 1f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.24f, 0.29f, 0.37f, 1f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.29f, 0.35f, 0.45f, 1f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.12f, 0.14f, 0.18f, 1f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.24f, 0.29f, 0.37f, 1f);
    }

    public void Render()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.Begin("##ERO_Main", flags);
        DrawTopBar();
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        const float footerHeight = 30f;
        var bodyHeight = Math.Max(1f, available.Y - footerHeight);

        DrawBody(bodyHeight);
        DrawFooter();
        ImGui.End();
    }

    private void DrawTopBar()
    {
        ImGui.BeginDisabled();
        ImGui.Button("Install Mod");
        ImGui.EndDisabled();
        ImGui.SameLine();

        if (ImGui.Button("Game Folder"))
        {
            PickGameFolder();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Profile:");
        ImGui.SameLine();
        ImGui.Text("Default");

        ImGui.SameLine();
        var label = _installation?.IsValid == true ? "Elden Ring configured" : "Game folder not configured";
        var color = _installation?.IsValid == true
            ? new Vector4(0.50f, 0.78f, 0.54f, 1f)
            : new Vector4(0.82f, 0.67f, 0.38f, 1f);
        ImGui.TextColored(color, label);
    }

    private void DrawBody(float height)
    {
        const float leftWidth = 365f;
        ImGui.BeginChild("InstalledMods", new Vector2(leftWidth, height), ImGuiChildFlags.Borders);
        ImGui.Text("Installed Mods");
        ImGui.Separator();

        if (_installation?.IsValid == true)
        {
            var enabled = true;
            ImGui.BeginDisabled();
            ImGui.Checkbox("##VanillaEnabled", ref enabled);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.Selectable("Vanilla Game Data", true);
            ImGui.SameLine();
            ImGui.TextDisabled("Base Game");
        }
        else
        {
            ImGui.TextDisabled("Select the Elden Ring Game folder to create the built-in Vanilla Game Data source.");
        }

        ImGui.EndChild();
        ImGui.SameLine();

        var rightWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.BeginChild("Inspector", new Vector2(rightWidth, height), ImGuiChildFlags.Borders);
        DrawInspector();
        ImGui.EndChild();
    }

    private void DrawInspector()
    {
        if (ImGui.BeginTabBar("InspectorTabs"))
        {
            if (ImGui.BeginTabItem("Overview"))
            {
                DrawOverview();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Files"))
            {
                ImGui.Text("File browsing returns in the next C# slice.");
                ImGui.TextDisabled("C1 does not scan the game or mod directories.");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("PARAMs"))
            {
                ImGui.Text("Smithbox PARAM integration is the next foundation milestone.");
                ImGui.TextDisabled("No regulation.bin parsing occurs in this bootstrap build.");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Conflicts"))
            {
                ImGui.Text("Conflict inspection is not active in C1.");
                ImGui.TextDisabled("No generated output or merge behavior is present.");
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawOverview()
    {
        ImGui.Text("Vanilla Game Data");
        ImGui.Separator();

        if (_installation is null)
        {
            ImGui.TextDisabled("No Elden Ring installation is configured.");
            return;
        }

        ImGui.TextDisabled("Game folder");
        ImGui.TextWrapped(_installation.Folder);
        ImGui.Spacing();

        DrawSentinel("eldenring.exe", _installation.HasExecutable, true);
        DrawSentinel("regulation.bin", _installation.HasRegulation, true);
        DrawSentinel("Data0.bhd", _installation.HasData0Header, false);
        DrawSentinel("Data0.bdt", _installation.HasData0Data, false);

        ImGui.Spacing();
        ImGui.TextDisabled("This check is path-only. Data0 is not opened, decrypted, indexed, or parsed here.");
    }

    private static void DrawSentinel(string name, bool present, bool required)
    {
        var color = present
            ? new Vector4(0.50f, 0.78f, 0.54f, 1f)
            : required
                ? new Vector4(0.90f, 0.40f, 0.40f, 1f)
                : new Vector4(0.82f, 0.67f, 0.38f, 1f);
        var state = present ? "Found" : required ? "Missing" : "Not found";
        ImGui.TextColored(color, $"{state,-10}");
        ImGui.SameLine();
        ImGui.Text(name);
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        var color = _statusIsError
            ? new Vector4(0.90f, 0.40f, 0.40f, 1f)
            : new Vector4(0.63f, 0.67f, 0.73f, 1f);
        ImGui.TextColored(color, _status);
        ImGui.SameLine();
        ImGui.TextDisabled($"   C#/.NET bootstrap   |   {_paths.Root}");
    }

    private void PickGameFolder()
    {
        var savedFolder = _settings.GameFolder;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Elden Ring Game folder",
            UseDescriptionForTitle = true,
            AutoUpgradeEnabled = true,
            ShowNewFolderButton = false,
            SelectedPath = !string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder)
                ? savedFolder
                : string.Empty
        };

        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            var candidate = GameInstallation.Inspect(dialog.SelectedPath);
            if (!candidate.IsValid)
            {
                _status = "That folder must contain eldenring.exe and regulation.bin.";
                _statusIsError = true;
                return;
            }

            _installation = candidate;
            _settings.GameFolder = candidate.Folder;
            _settingsStore.Save(_settings);
            _status = "Game folder saved. No game archives were opened.";
            _statusIsError = false;
        }
        catch (Exception ex)
        {
            _status = $"Could not save Game folder: {ex.Message}";
            _statusIsError = true;
        }
    }
}
