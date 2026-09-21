using EldenRingOrganizer.Configuration;
using EldenRingOrganizer.SmithboxIntegration;
using EldenRingOrganizer.Storage;
using Hexa.NET.ImGui;
using System.Numerics;
using System.Windows.Forms;

namespace EldenRingOrganizer.UI;

public sealed class OrganizerShell : IDisposable
{
    private readonly OrganizerPaths _paths;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly ModCatalog _modCatalog;
    private readonly ReadOnlyParamEditorPanel _paramPanel = new();
    private readonly ReadOnlyTextEditorPanel _textPanel = new();
    private IReadOnlyList<InstalledMod> _mods = [];
    private GameInstallation? _installation;
    private InstalledMod? _selectedMod;
    private SmithboxParamSession? _dataSession;
    private Task<SmithboxParamSession>? _dataLoadTask;
    private Task<InstalledMod>? _modInstallTask;
    private bool _autoLoadAttempted;
    private string _fileSearch = "";
    private string? _selectedFilePath;
    private string _filePreviewText = "";
    private string _filePreviewMessage = "Select a readable text file to preview it here.";
    private string _status = "Ready";
    private bool _statusIsError;

    public OrganizerShell(string root)
    {
        _paths = new OrganizerPaths(root);
        _paths.EnsureCreated();
        _settingsStore = new SettingsStore(_paths.Settings);
        _settings = _settingsStore.Load();
        _modCatalog = new ModCatalog(_paths.Mods, _paths.Cache);
        _mods = _modCatalog.Refresh();

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
        PollModInstall();
        PollDataLoad();

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
        if (_dataLoadTask is not null || _modInstallTask is not null)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Install Mod"))
        {
            PickModArchive();
        }

        if (_dataLoadTask is not null || _modInstallTask is not null)
        {
            ImGui.EndDisabled();
        }

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

        var vanillaEnabled = true;
        ImGui.BeginDisabled();
        ImGui.Checkbox("##VanillaEnabled", ref vanillaEnabled);
        ImGui.EndDisabled();
        ImGui.SameLine();

        var vanillaSelected = _selectedMod is null;
        if (ImGui.Selectable("Vanilla Game Data", vanillaSelected) && !vanillaSelected)
        {
            SelectVanilla();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Base Game");

        if (_mods.Count > 0)
        {
            ImGui.Separator();
        }

        for (var i = 0; i < _mods.Count; i++)
        {
            var mod = _mods[i];
            var enabled = mod.Enabled;

            if (ImGui.Checkbox($"##modEnabled_{i}", ref enabled))
            {
                try
                {
                    _modCatalog.SetEnabled(mod, enabled);
                    _status = $"{mod.Name} {(enabled ? "enabled" : "disabled")}.";
                    _statusIsError = false;
                }
                catch (Exception ex)
                {
                    _status = $"Could not update {mod.Name}: {GetRootMessage(ex)}";
                    _statusIsError = true;
                }
            }

            ImGui.SameLine();

            var selected = _selectedMod is not null &&
                           string.Equals(_selectedMod.RootPath, mod.RootPath, StringComparison.OrdinalIgnoreCase);

            if (ImGui.Selectable($"{mod.Name}##mod_{i}", selected) && !selected)
            {
                SelectMod(mod);
            }

            ImGui.SameLine();

            if (mod.HasRegulation && mod.HasText)
            {
                ImGui.TextDisabled("PARAM + FMG");
            }
            else if (mod.HasRegulation)
            {
                ImGui.TextDisabled("PARAM");
            }
            else if (mod.HasText)
            {
                ImGui.TextDisabled("FMG");
            }
            else
            {
                ImGui.TextDisabled("Files");
            }
        }

        if (_mods.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Install a ZIP, 7z, or RAR mod archive to add an isolated source.");
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
                DrawFiles();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("PARAMs"))
            {
                DrawParams();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Text"))
            {
                DrawText();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Conflicts"))
            {
                ImGui.Text("Cross-mod semantic conflict resolution is reserved for the next merge/deployment slice.");
                ImGui.TextDisabled("Selected-source versus vanilla PARAM and FMG comparison is active now.");
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawParams()
    {
        if (!PrepareDataInspector())
        {
            return;
        }

        DrawReloadButton();
        ImGui.SameLine();
        ImGui.TextDisabled("Read-only. Smithbox PARAM data is compared against Vanilla Game Data.");
        ImGui.Separator();

        if (_dataLoadTask is not null || _modInstallTask is not null)
        {
            DrawLoadingState();
            return;
        }

        _paramPanel.Render();
    }

    private void DrawText()
    {
        if (!PrepareDataInspector())
        {
            return;
        }

        DrawReloadButton();
        ImGui.SameLine();
        ImGui.TextDisabled("Read-only. Smithbox TextData and vanilla FMG comparison are active.");
        ImGui.Separator();

        if (_dataLoadTask is not null || _modInstallTask is not null)
        {
            DrawLoadingState();
            return;
        }

        _textPanel.Render();
    }

    private bool PrepareDataInspector()
    {
        if (_installation?.IsValid != true)
        {
            ImGui.TextDisabled("Configure the Elden Ring Game folder first.");
            return false;
        }

        if (!_autoLoadAttempted && _dataSession is null && _dataLoadTask is null)
        {
            _autoLoadAttempted = true;
            StartCurrentSourceLoad();
        }

        return true;
    }

    private void DrawReloadButton()
    {
        if (_dataLoadTask is not null || _modInstallTask is not null)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Reload Source"))
        {
            StartCurrentSourceLoad();
        }

        if (_dataLoadTask is not null || _modInstallTask is not null)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawLoadingState()
    {
        ImGui.Text($"Loading Smithbox game data for {GetSelectedSourceName()}...");
        ImGui.TextDisabled("PARAMs, row names, TextData, FMGs, metadata, and vanilla comparison caches are loading off the UI thread.");
    }

    private void DrawOverview()
    {
        ImGui.Text(GetSelectedSourceName());
        ImGui.Separator();

        if (_selectedMod is not null)
        {
            ImGui.TextDisabled("Managed mod root");
            ImGui.TextWrapped(_selectedMod.RootPath);
            ImGui.Spacing();

            DrawSentinel("regulation.bin", _selectedMod.HasRegulation, false);
            DrawSentinel("msg", _selectedMod.HasText, false);

            if (!string.IsNullOrWhiteSpace(_selectedMod.InstalledFrom))
            {
                ImGui.Spacing();
                ImGui.TextDisabled($"Installed from: {_selectedMod.InstalledFrom}");
            }

            ImGui.TextDisabled($"Enabled: {(_selectedMod.Enabled ? "Yes" : "No")}");
        }
        else if (_installation is not null)
        {
            ImGui.TextDisabled("Game folder");
            ImGui.TextWrapped(_installation.Folder);
            ImGui.Spacing();

            DrawSentinel("eldenring.exe", _installation.HasExecutable, true);
            DrawSentinel("regulation.bin", _installation.HasRegulation, true);
            DrawSentinel("Data0.bhd", _installation.HasData0Header, false);
            DrawSentinel("Data0.bdt", _installation.HasData0Data, false);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Installed mods stay inside the Organizer mods directory. This slice does not deploy files into Elden Ring.");
    }

    private void DrawFiles()
    {
        if (_selectedMod is null)
        {
            ImGui.Text("Vanilla Game Data");
            ImGui.Separator();
            ImGui.TextDisabled("The vanilla installation is the immutable baseline. Managed file listing is shown for installed mods.");
            return;
        }

        ImGui.Text(_selectedMod.Name);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##fileSearch", "Search installed files", ref _fileSearch, 512);
        ImGui.Separator();

        var files = Directory.EnumerateFiles(_selectedMod.RootPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info =>
            {
                var relative = Path.GetRelativePath(_selectedMod.RootPath, info.FullName);
                return !string.Equals(relative, "ero.mod.json", StringComparison.OrdinalIgnoreCase) &&
                       (string.IsNullOrWhiteSpace(_fileSearch) ||
                        relative.Contains(_fileSearch, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(info => Path.GetRelativePath(_selectedMod.RootPath, info.FullName), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var available = ImGui.GetContentRegionAvail();
        var listWidth = Math.Clamp(available.X * 0.44f, 320f, 620f);

        ImGui.BeginChild("InstalledFileList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders);

        const ImGuiTableFlags fileTableFlags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("InstalledFilesTable", 2, fileTableFlags))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("File", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableHeadersRow();

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(_selectedMod.RootPath, file.FullName);
                var selected = string.Equals(_selectedFilePath, file.FullName, StringComparison.OrdinalIgnoreCase);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                if (ImGui.Selectable($"{relative}##installed_file_{relative}", selected))
                {
                    SelectFilePreview(file);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(relative);
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled(FormatBytes(file.Length));
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild("InstalledFilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders);
        RenderFilePreview();
        ImGui.EndChild();
    }

    private void SelectFilePreview(FileInfo file)
    {
        _selectedFilePath = file.FullName;
        _filePreviewText = "";

        if (!IsReadableTextFile(file.Extension))
        {
            _filePreviewMessage = file.Extension.Equals(".dcx", StringComparison.OrdinalIgnoreCase)
                ? "DCX container browsing is reserved for the native container-browser integration. This build does not unpack it to disk."
                : "No read-only text preview is available for this file type.";
            return;
        }

        try
        {
            const int maxChars = 4 * 1024 * 1024;
            using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[maxChars];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            _filePreviewText = new string(buffer, 0, read);

            if (!reader.EndOfStream)
            {
                _filePreviewText += "\r\n\r\n[Preview truncated after 4 MiB of text.]";
            }

            _filePreviewMessage = "";
        }
        catch (Exception ex)
        {
            _filePreviewMessage = $"Could not preview file: {GetRootMessage(ex)}";
        }
    }

    private void RenderFilePreview()
    {
        ImGui.Text("Read-only Preview");
        ImGui.Separator();

        if (_selectedFilePath is null)
        {
            ImGui.TextDisabled(_filePreviewMessage);
            return;
        }

        ImGui.TextWrapped(Path.GetFileName(_selectedFilePath));
        ImGui.TextDisabled(_selectedFilePath);
        ImGui.Separator();

        if (!string.IsNullOrWhiteSpace(_filePreviewMessage))
        {
            ImGui.TextDisabled(_filePreviewMessage);
            return;
        }

        ImGui.InputTextMultiline(
            "##readonlyFilePreview",
            ref _filePreviewText,
            StudioCore.Application.GUI.GetTextInputBuffer(_filePreviewText),
            new Vector2(-1, -1),
            ImGuiInputTextFlags.ReadOnly);
    }

    private static bool IsReadableTextFile(string extension)
    {
        return extension.ToLowerInvariant() is
            ".hks" or
            ".txt" or
            ".ini" or
            ".cfg" or
            ".json" or
            ".toml" or
            ".xml" or
            ".yaml" or
            ".yml" or
            ".md" or
            ".log";
    }

    private void ResetFilePreview()
    {
        _selectedFilePath = null;
        _filePreviewText = "";
        _filePreviewMessage = "Select a readable text file to preview it here.";
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
        ImGui.TextDisabled($"   0.1D Mod Inspection   |   {_paths.Root}");
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
            ResetDataSession();
            _autoLoadAttempted = false;
            _status = "Game folder saved. Vanilla is the comparison baseline for PARAM and FMG inspection.";
            _statusIsError = false;
        }
        catch (Exception ex)
        {
            _status = $"Could not save Game folder: {GetRootMessage(ex)}";
            _statusIsError = true;
        }
    }

    private void PickModArchive()
    {
        if (_modInstallTask is not null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Install Elden Ring mod archive",
            Filter = "Supported mod archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|ZIP archives (*.zip)|*.zip|7-Zip archives (*.7z)|*.7z|RAR archives (*.rar)|*.rar|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var archivePath = dialog.FileName;
        _status = $"Installing {Path.GetFileName(archivePath)}...";
        _statusIsError = false;
        _modInstallTask = Task.Run(() => _modCatalog.InstallArchive(archivePath));
    }

    private void PollModInstall()
    {
        if (_modInstallTask is not { IsCompleted: true } completed)
        {
            return;
        }

        _modInstallTask = null;

        try
        {
            var installed = completed.GetAwaiter().GetResult();
            _mods = _modCatalog.Refresh();
            _selectedMod = _mods.FirstOrDefault(x =>
                string.Equals(x.RootPath, installed.RootPath, StringComparison.OrdinalIgnoreCase)) ?? installed;
            _fileSearch = "";
            ResetFilePreview();
            ResetDataSession();
            _autoLoadAttempted = true;

            if (_installation?.IsValid == true)
            {
                StartCurrentSourceLoad();
                _status = $"Installed {installed.Name}. Loading its Smithbox PARAM and FMG comparison.";
            }
            else
            {
                _status = $"Installed {installed.Name}. Configure the Elden Ring Game folder to inspect it against vanilla.";
            }

            _statusIsError = false;
        }
        catch (Exception ex)
        {
            _mods = _modCatalog.Refresh();
            _status = $"Mod install failed: {GetRootMessage(ex)}";
            _statusIsError = true;
        }
    }

    private void SelectVanilla()
    {
        _selectedMod = null;
        _fileSearch = "";
        ResetFilePreview();
        ResetDataSession();
        _autoLoadAttempted = true;

        if (_installation?.IsValid == true)
        {
            StartCurrentSourceLoad();
        }
    }

    private void SelectMod(InstalledMod mod)
    {
        _selectedMod = mod;
        _fileSearch = "";
        ResetFilePreview();
        ResetDataSession();
        _autoLoadAttempted = true;

        if (_installation?.IsValid == true)
        {
            StartCurrentSourceLoad();
        }
    }

    private void StartCurrentSourceLoad()
    {
        if (_installation?.IsValid != true || _dataLoadTask is not null || _modInstallTask is not null)
        {
            return;
        }

        string projectPath;
        string sourceName;
        string? sourceRegulationPath;

        if (_selectedMod is null)
        {
            projectPath = Path.Combine(_paths.Cache, "smithbox", "vanilla");
            sourceName = "Vanilla Game Data";
            sourceRegulationPath = null;
        }
        else
        {
            projectPath = _selectedMod.RootPath;
            sourceName = _selectedMod.Name;
            var regulation = Path.Combine(_selectedMod.RootPath, "regulation.bin");
            sourceRegulationPath = File.Exists(regulation) ? regulation : null;
        }

        _status = $"Loading Smithbox game data for {sourceName}...";
        _statusIsError = false;
        _dataLoadTask = SmithboxParamSession.LoadAsync(
            _installation.Folder,
            projectPath,
            sourceName,
            sourceRegulationPath);
    }

    private void PollDataLoad()
    {
        if (_dataLoadTask is not { IsCompleted: true } completed)
        {
            return;
        }

        _dataLoadTask = null;

        try
        {
            var session = completed.GetAwaiter().GetResult();
            var previous = _dataSession;
            _dataSession = session;
            _paramPanel.SetSession(session);
            _textPanel.SetSession(session);
            previous?.Dispose();

            _status = $"{session.SourceName} loaded. PARAM and FMG values are compared against Vanilla Game Data.";
            _statusIsError = false;
        }
        catch (Exception ex)
        {
            _status = $"Game-data load failed: {GetRootMessage(ex)}";
            _statusIsError = true;
        }
    }

    private void ResetDataSession()
    {
        _paramPanel.SetSession(null);
        _textPanel.SetSession(null);
        _dataSession?.Dispose();
        _dataSession = null;
    }

    private string GetSelectedSourceName()
    {
        return _selectedMod?.Name ?? "Vanilla Game Data";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes / (1024d * 1024d):0.0} MB";
    }

    private static string GetRootMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    public void Dispose()
    {
        ResetDataSession();
    }
}
