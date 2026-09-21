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
    private readonly Dictionary<int, Dictionary<int, FMG.Entry>> _vanillaEntriesByFmg = new();

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
        _vanillaEntriesByFmg.Clear();

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
        ImGui.TextDisabled("  |  Baseline: Vanilla Game Data  |  Smithbox grouped FMG view  |  Read-only");
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        var containerWidth = Math.Clamp(available.X * 0.18f, 200f, 310f);
        var fmgWidth = Math.Clamp(available.X * 0.23f, 250f, 400f);

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

        var wrappers = _selectedContainer.FmgWrappers ?? [];

        foreach (var fmg in wrappers.OrderBy(x => x.ID))
        {
            var displayName = TextUtils.GetFmgDisplayName(
                _session!.Project,
                _selectedContainer,
                fmg.ID,
                fmg.Name);

            var grouping = TextUtils.GetFmgGrouping(
                _session.Project,
                _selectedContainer,
                fmg.ID,
                fmg.Name);

            if (!Matches($"{displayName} {fmg.Name} {fmg.ID} {grouping}", _fmgSearch))
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(grouping) || grouping == "Unknown"
                ? displayName
                : $"{displayName}  [{grouping}]";

            if (ImGui.Selectable(
                    $"{label}##fmg_{fmg.ID}_{fmg.Name}",
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
        var listHeight = Math.Max(150f, region.Y * 0.36f);

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
                SelectEntry(entry);
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
        if (_session is null || _selectedFmg is null || _selectedEntry is null)
        {
            ImGui.TextDisabled("Select an FMG entry.");
            return;
        }

        var parts = GetComparisonParts();
        if (parts.Count == 0)
        {
            ImGui.TextDisabled("No text data is available for this entry.");
            return;
        }

        var overallState = GetOverallState(parts);
        var stateLabel = overallState switch
        {
            ComparisonState.Added => "NEW ENTRY",
            ComparisonState.Modified => "MODIFIED",
            _ => "UNCHANGED"
        };

        if (overallState is ComparisonState.Unchanged)
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

        if (parts.Count > 1)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("  |  Smithbox grouped item text");
        }

        ImGui.Separator();

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("ReadonlyFmgComparison", 3, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 95f);
        ImGui.TableSetupColumn(_session.SourceName, ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Vanilla", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableHeadersRow();

        foreach (var part in parts)
        {
            var state = GetPartState(part.PrimaryEntry, part.VanillaEntry);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);

            if (state is ComparisonState.Unchanged)
            {
                ImGui.TextUnformatted(part.Label);
            }
            else
            {
                ImGui.TextColored(
                    StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text,
                    part.Label);
            }

            ImGui.TableSetColumnIndex(1);
            RenderTextCell(part.PrimaryEntry?.Text, state is not ComparisonState.Unchanged, false);

            ImGui.TableSetColumnIndex(2);
            RenderTextCell(part.VanillaEntry?.Text, false, part.VanillaEntry is null);
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
        _vanillaEntriesByFmg.Clear();
        _fmgSearch = "";
        _entrySearch = "";

        var selection = _session.TextView.Selection;
        selection.SelectedFileDictionaryEntry = container.FileEntry;
        selection.SelectedContainerWrapper = container;
        selection.SelectedContainerKey = 0;

        var wrappers = container.FmgWrappers ?? [];
        var first = wrappers.OrderBy(x => x.ID).FirstOrDefault();
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

        var first = fmg.File.Entries.FirstOrDefault();
        if (first is not null)
        {
            SelectEntry(first);
        }

        EnsureSelectedEntryVisible();
    }

    private void SelectEntry(FMG.Entry entry)
    {
        if (_session is null || _selectedFmg is null)
        {
            return;
        }

        _selectedEntry = entry;
        var index = _selectedFmg.File.Entries.IndexOf(entry);
        if (index >= 0)
        {
            _session.TextView.Selection.SelectFmgEntry(index, entry);
        }
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

        var entry = _selectedFmg.File.Entries.FirstOrDefault(candidate =>
            _entryFilter.Matches(GetState(candidate)) &&
            Matches($"{candidate.ID} {candidate.Text}", _entrySearch));

        if (entry is not null)
        {
            SelectEntry(entry);
        }
        else
        {
            _selectedEntry = null;
        }
    }

    private ComparisonState GetState(FMG.Entry entry)
    {
        if (_session is null || _selectedFmg is null)
        {
            return ComparisonState.Unchanged;
        }

        if (FindVanillaEntry(_selectedFmg.ID, entry.ID) is null ||
            _session.TextView.DifferenceManager.IsUniqueToProject(entry))
        {
            return ComparisonState.Added;
        }

        if (_session.TextView.DifferenceManager.IsDifferentToVanilla(entry))
        {
            return ComparisonState.Modified;
        }

        return ComparisonState.Unchanged;
    }

    private List<TextPart> GetComparisonParts()
    {
        if (_session is null || _selectedFmg is null || _selectedEntry is null)
        {
            return [];
        }

        var manager = _session.TextView.EntryGroupManager;
        var group = manager.GetEntryGroup(_selectedEntry);

        if (group is null || !group.SupportsGrouping)
        {
            return
            [
                CreatePart("Text", _selectedFmg)
            ];
        }

        var parts = new List<TextPart>();

        if (group.SupportsTitle)
        {
            parts.Add(CreatePart("Title", manager.GetAssociatedTitleWrapper(_selectedFmg.ID)));
        }

        if (group.SupportsSummary)
        {
            parts.Add(CreatePart("Summary", manager.GetAssociatedSummaryWrapper(_selectedFmg.ID)));
        }

        if (group.SupportsDescription)
        {
            parts.Add(CreatePart("Description", manager.GetAssociatedDescriptionWrapper(_selectedFmg.ID)));
        }

        if (group.SupportsEffect)
        {
            parts.Add(CreatePart("Effect", manager.GetAssociatedEffectWrapper(_selectedFmg.ID)));
        }

        return parts;
    }

    private TextPart CreatePart(string label, TextFmgWrapper? wrapper)
    {
        if (_selectedEntry is null)
        {
            return new TextPart(label, null, null);
        }

        var primaryEntry = wrapper?.File.Entries.FirstOrDefault(x => x.ID == _selectedEntry.ID);
        var vanillaEntry = wrapper is null ? null : FindVanillaEntry(wrapper.ID, _selectedEntry.ID);
        return new TextPart(label, primaryEntry, vanillaEntry);
    }

    private ComparisonState GetOverallState(IEnumerable<TextPart> parts)
    {
        var sawModified = false;

        foreach (var part in parts)
        {
            var state = GetPartState(part.PrimaryEntry, part.VanillaEntry);
            if (state is ComparisonState.Added)
            {
                return ComparisonState.Added;
            }

            if (state is ComparisonState.Modified)
            {
                sawModified = true;
            }
        }

        return sawModified ? ComparisonState.Modified : ComparisonState.Unchanged;
    }

    private static ComparisonState GetPartState(FMG.Entry? primary, FMG.Entry? vanilla)
    {
        if (primary is not null && vanilla is null)
        {
            return ComparisonState.Added;
        }

        if (primary is null && vanilla is not null)
        {
            return ComparisonState.Modified;
        }

        return string.Equals(primary?.Text, vanilla?.Text, StringComparison.Ordinal)
            ? ComparisonState.Unchanged
            : ComparisonState.Modified;
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

    private FMG.Entry? FindVanillaEntry(int fmgId, int entryId)
    {
        if (_session is null || _selectedContainer is null)
        {
            return null;
        }

        if (!_vanillaEntriesByFmg.TryGetValue(fmgId, out var entries))
        {
            entries = new Dictionary<int, FMG.Entry>();
            _vanillaEntriesByFmg[fmgId] = entries;

            var container = FindVanillaContainer(_selectedContainer);
            if (container is null)
            {
                return null;
            }

            if (container.FmgWrappers is null || container.FmgWrappers.Count == 0)
            {
                _session.TextData.VanillaBank.LoadFmgWrappers(container);
            }

            var wrappers = container.FmgWrappers ?? [];
            var fmg = wrappers.FirstOrDefault(x => x.ID == fmgId);

            if (fmg is not null)
            {
                foreach (var entry in fmg.File.Entries)
                {
                    entries.TryAdd(entry.ID, entry);
                }
            }
        }

        return entries.TryGetValue(entryId, out var result) ? result : null;
    }

    private static void RenderTextCell(string? text, bool changed, bool missing)
    {
        if (missing)
        {
            ImGui.TextDisabled("— Not present in Vanilla Game Data —");
            return;
        }

        if (text is null)
        {
            ImGui.TextDisabled("—");
            return;
        }

        if (changed)
        {
            ImGui.PushStyleColor(
                ImGuiCol.Text,
                StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text);
        }

        ImGui.TextWrapped(text);

        if (changed)
        {
            ImGui.PopStyleColor();
        }
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

    private sealed record TextPart(
        string Label,
        FMG.Entry? PrimaryEntry,
        FMG.Entry? VanillaEntry);
}
