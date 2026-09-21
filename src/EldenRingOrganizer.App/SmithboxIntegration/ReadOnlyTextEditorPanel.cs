using Hexa.NET.ImGui;
using SoulsFormats;
using StudioCore.Editors.TextEditor;
using System.Numerics;

namespace EldenRingOrganizer.SmithboxIntegration;

public sealed class ReadOnlyTextEditorPanel
{
    private SmithboxParamSession? _session;
    private string _containerSearch = "";
    private string _fmgSearch = "";
    private string _entrySearch = "";
    private ComparisonFilter _entryFilter = ComparisonFilter.All;
    private TextContainerWrapper? _selectedContainer;
    private TextFmgWrapper? _selectedFmg;
    private FMG.Entry? _selectedEntry;

    public void SetSession(SmithboxParamSession? session)
    {
        _session = session;
        _containerSearch = "";
        _fmgSearch = "";
        _entrySearch = "";
        _entryFilter = ComparisonFilter.All;
        _selectedContainer = null;
        _selectedFmg = null;
        _selectedEntry = null;

        if (session is null)
        {
            return;
        }

        var first = GetContainers().FirstOrDefault();
        if (first is not null)
        {
            SelectContainer(first);
        }
    }

    public void Render()
    {
        if (_session is null)
        {
            ImGui.TextDisabled("No Text source is loaded.");
            return;
        }

        ImGui.Text($"Selected Source: {_session.SourceName}");
        ImGui.SameLine();
        ImGui.TextDisabled("  |  Baseline: Vanilla Game Data  |  Smithbox FMG view  |  Read-only");
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        var containerWidth = Math.Clamp(available.X * 0.20f, 210f, 330f);
        var fmgWidth = Math.Clamp(available.X * 0.25f, 250f, 420f);

        ImGui.BeginChild("TextContainers", new Vector2(containerWidth, available.Y), ImGuiChildFlags.Borders);
        RenderContainers();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("TextFmgs", new Vector2(fmgWidth, available.Y), ImGuiChildFlags.Borders);
        RenderFmgs();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("TextEntries", new Vector2(0, available.Y), ImGuiChildFlags.Borders);
        RenderEntriesAndComparison();
        ImGui.EndChild();
    }

    private void RenderContainers()
    {
        ImGui.Text("Text Containers");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##textContainerFilter", "Search Menu, Item, filename", ref _containerSearch, 256);
        ImGui.Separator();

        foreach (var container in GetContainers())
        {
            var display = container.GetContainerDisplayName();
            var fileName = container.FileEntry.Filename;

            if (!Matches($"{display} {fileName} {container.FileEntry.Path}", _containerSearch))
            {
                continue;
            }

            if (ImGui.Selectable($"{display}##text_container_{fileName}", ReferenceEquals(_selectedContainer, container)))
            {
                SelectContainer(container);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(container.FileEntry.Path);
            }
        }
    }

    private void RenderFmgs()
    {
        ImGui.Text(_selectedContainer is null
            ? "FMGs"
            : $"FMGs — {_selectedContainer.GetContainerDisplayName()}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##fmgFilter", "Search FMG name or ID", ref _fmgSearch, 256);
        ImGui.Separator();

        if (_selectedContainer is null)
        {
            ImGui.TextDisabled("Select a text container.");
            return;
        }

        foreach (var fmg in _selectedContainer.FmgWrappers.OrderBy(x => x.ID))
        {
            var displayName = TextUtils.GetFmgDisplayName(
                _session!.Project,
                _selectedContainer,
                fmg.ID,
                fmg.Name);

            if (!Matches($"{displayName} {fmg.Name} {fmg.ID}", _fmgSearch))
            {
                continue;
            }

            if (ImGui.Selectable(
                    $"{displayName}##fmg_{fmg.ID}_{fmg.Name}",
                    ReferenceEquals(_selectedFmg, fmg)))
            {
                SelectFmg(fmg);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{fmg.Name}  |  Binder ID {fmg.ID}");
            }
        }
    }

    private void RenderEntriesAndComparison()
    {
        ImGui.Text(_selectedFmg is null ? "Entries" : $"Entries — {_selectedFmg.Name}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##fmgEntryFilter", "Search entry ID or text", ref _entrySearch, 512);
        RenderComparisonFilter();
        ImGui.Separator();

        if (_selectedFmg is null)
        {
            ImGui.TextDisabled("Select an FMG.");
            return;
        }

        var region = ImGui.GetContentRegionAvail();
        var listHeight = Math.Max(150f, region.Y * 0.43f);

        ImGui.BeginChild("FmgEntryList", new Vector2(0, listHeight), ImGuiChildFlags.Borders);

        foreach (var entry in _selectedFmg.File.Entries)
        {
            var state = GetState(entry);

            if (!_entryFilter.Matches(state))
            {
                continue;
            }

            if (!Matches($"{entry.ID} {entry.Text}", _entrySearch))
            {
                continue;
            }

            var preview = NormalizePreview(entry.Text);
            var prefix = state is ComparisonState.Added ? "[NEW] " : "";
            var label = $"{prefix}{entry.ID}  {preview}##fmg_entry_{entry.ID}";

            if (state is not ComparisonState.Unchanged)
            {
                ImGui.PushStyleColor(
                    ImGuiCol.Text,
                    StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text);
            }

            if (ImGui.Selectable(label, ReferenceEquals(_selectedEntry, entry)))
            {
                _selectedEntry = entry;
            }

            if (state is not ComparisonState.Unchanged)
            {
                ImGui.PopStyleColor();
            }
        }

        ImGui.EndChild();
        ImGui.Spacing();
        RenderComparison();
    }

    private void RenderComparison()
    {
        if (_selectedEntry is null)
        {
            ImGui.TextDisabled("Select an FMG entry.");
            return;
        }

        var state = GetState(_selectedEntry);
        var vanillaEntry = FindVanillaEntry(_selectedEntry.ID);

        var stateLabel = state switch
        {
            ComparisonState.Added => "NEW ENTRY",
            ComparisonState.Modified => "MODIFIED",
            _ => "UNCHANGED"
        };

        if (state is ComparisonState.Unchanged)
        {
            ImGui.TextDisabled(stateLabel);
        }
        else
        {
            ImGui.TextColored(
                StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text,
                stateLabel);
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"ID {_selectedEntry.ID}");
        ImGui.Separator();

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("ReadonlyFmgComparison", 2, flags))
        {
            return;
        }

        ImGui.TableSetupColumn(_session!.SourceName, ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Vanilla", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableHeadersRow();

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextWrapped(_selectedEntry.Text ?? "");

        ImGui.TableSetColumnIndex(1);
        if (vanillaEntry is null)
        {
            ImGui.TextDisabled("— Not present in Vanilla Game Data —");
        }
        else
        {
            ImGui.TextWrapped(vanillaEntry.Text ?? "");
        }

        ImGui.EndTable();
    }

    private void RenderComparisonFilter()
    {
        RenderFilterChoice("All", ComparisonFilter.All);
        ImGui.SameLine();
        RenderFilterChoice("Changed", ComparisonFilter.Changed);
        ImGui.SameLine();
        RenderFilterChoice("Added", ComparisonFilter.Added);
        ImGui.SameLine();
        RenderFilterChoice("Unchanged", ComparisonFilter.Unchanged);
    }

    private void RenderFilterChoice(string label, ComparisonFilter filter)
    {
        if (ImGui.Selectable($"{label}##textState_{label}", _entryFilter == filter))
        {
            _entryFilter = filter;
            EnsureSelectedEntryVisible();
        }
    }

    private IEnumerable<TextContainerWrapper> GetContainers()
    {
        if (_session is null)
        {
            return [];
        }

        return _session.TextData.PrimaryBank.Containers.Values
            .Where(x => x.ContainerDisplayCategory == TextContainerCategory.English)
            .Where(x => !x.IsContainerUnused())
            .OrderBy(x => x.GetContainerDisplayName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FileEntry.Filename, StringComparer.OrdinalIgnoreCase);
    }

    private void SelectContainer(TextContainerWrapper container)
    {
        if (_session is null)
        {
            return;
        }

        if (container.FmgWrappers is null || container.FmgWrappers.Count == 0)
        {
            _session.TextData.PrimaryBank.LoadFmgWrappers(container);
        }

        var vanillaContainer = FindVanillaContainer(container);
        if (vanillaContainer is not null &&
            (vanillaContainer.FmgWrappers is null || vanillaContainer.FmgWrappers.Count == 0))
        {
            _session.TextData.VanillaBank.LoadFmgWrappers(vanillaContainer);
        }

        _selectedContainer = container;
        _selectedFmg = null;
        _selectedEntry = null;
        _fmgSearch = "";
        _entrySearch = "";

        var selection = _session.TextView.Selection;
        selection.SelectedFileDictionaryEntry = container.FileEntry;
        selection.SelectedContainerWrapper = container;
        selection.SelectedContainerKey = 0;

        var first = container.FmgWrappers.OrderBy(x => x.ID).FirstOrDefault();
        if (first is not null)
        {
            SelectFmg(first);
        }
    }

    private void SelectFmg(TextFmgWrapper fmg)
    {
        if (_session is null)
        {
            return;
        }

        _selectedFmg = fmg;
        _selectedEntry = null;
        _entrySearch = "";

        _session.TextView.Selection.SelectFmg(fmg, false);
        _selectedEntry = fmg.File.Entries.FirstOrDefault();
        EnsureSelectedEntryVisible();
    }

    private void EnsureSelectedEntryVisible()
    {
        if (_selectedFmg is null)
        {
            _selectedEntry = null;
            return;
        }

        if (_selectedEntry is not null &&
            _entryFilter.Matches(GetState(_selectedEntry)) &&
            Matches($"{_selectedEntry.ID} {_selectedEntry.Text}", _entrySearch))
        {
            return;
        }

        _selectedEntry = _selectedFmg.File.Entries.FirstOrDefault(entry =>
            _entryFilter.Matches(GetState(entry)) &&
            Matches($"{entry.ID} {entry.Text}", _entrySearch));
    }

    private ComparisonState GetState(FMG.Entry entry)
    {
        if (_session is null)
        {
            return ComparisonState.Unchanged;
        }

        if (_session.TextView.DifferenceManager.IsUniqueToProject(entry))
        {
            return ComparisonState.Added;
        }

        if (_session.TextView.DifferenceManager.IsDifferentToVanilla(entry))
        {
            return ComparisonState.Modified;
        }

        return ComparisonState.Unchanged;
    }

    private TextContainerWrapper? FindVanillaContainer(TextContainerWrapper primary)
    {
        if (_session is null)
        {
            return null;
        }

        return _session.TextData.VanillaBank.Containers.Values.FirstOrDefault(x =>
            x.ContainerDisplayCategory == primary.ContainerDisplayCategory &&
            string.Equals(
                x.FileEntry.Filename,
                primary.FileEntry.Filename,
                StringComparison.OrdinalIgnoreCase));
    }

    private FMG.Entry? FindVanillaEntry(int id)
    {
        if (_session is null || _selectedContainer is null || _selectedFmg is null)
        {
            return null;
        }

        var container = FindVanillaContainer(_selectedContainer);
        if (container is null)
        {
            return null;
        }

        if (container.FmgWrappers is null || container.FmgWrappers.Count == 0)
        {
            _session.TextData.VanillaBank.LoadFmgWrappers(container);
        }

        var fmg = container.FmgWrappers.FirstOrDefault(x => x.ID == _selectedFmg.ID);
        return fmg?.File.Entries.FirstOrDefault(x => x.ID == id);
    }

    private static bool Matches(string text, string filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
               text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return preview.Length <= 80 ? preview : $"{preview[..77]}...";
    }
}
