using EldenRingOrganizer.Semantic;
using Hexa.NET.ImGui;
using System.Numerics;

namespace EldenRingOrganizer.UI;

public sealed class SemanticParamEditorPanel
{
    private RegulationIndex? _index;
    private string _paramSearch = "";
    private string _rowSearch = "";
    private string _fieldSearch = "";
    private bool _changedOnly = true;
    private string? _selectedParamName;
    private int? _selectedRowId;
    private int _selectedOccurrence;

    public void SetIndex(RegulationIndex? index)
    {
        _index = index;
        _paramSearch = "";
        _rowSearch = "";
        _fieldSearch = "";
        _changedOnly = true;
        _selectedParamName = null;
        _selectedRowId = null;
        _selectedOccurrence = 0;

        if (index is null)
        {
            return;
        }

        _selectedParamName = GetParamItems()
            .FirstOrDefault(x => x.Delta is not null)?.Name
            ?? GetParamItems().FirstOrDefault()?.Name;
        SelectFirstVisibleRow();
    }

    public void Render()
    {
        if (_index is null)
        {
            ImGui.TextDisabled("No semantic regulation index is loaded.");
            return;
        }

        ImGui.Text($"Selected Source: {_index.Document.SourceName}");
        ImGui.SameLine();
        ImGui.TextDisabled(
            $"  |  Regulation {_index.Document.RegulationVersionDisplay}  |  Vanilla comparison  |  Cached semantic view");
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        var paramsWidth = Math.Clamp(available.X * 0.22f, 220f, 360f);
        var rowsWidth = Math.Clamp(available.X * 0.28f, 280f, 460f);

        ImGui.BeginChild("SemanticParamBrowser", new Vector2(paramsWidth, available.Y), ImGuiChildFlags.Borders);
        RenderParamList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("SemanticRowBrowser", new Vector2(rowsWidth, available.Y), ImGuiChildFlags.Borders);
        RenderRowList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("SemanticFieldBrowser", new Vector2(0, available.Y), ImGuiChildFlags.Borders);
        RenderFieldList();
        ImGui.EndChild();
    }

    private void RenderParamList()
    {
        ImGui.Text("PARAMs");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##semanticParamFilter", "Search name or modified", ref _paramSearch, 256);

        if (ImGui.Checkbox("Changed only##semanticChangedOnly", ref _changedOnly))
        {
            EnsureSelectionVisible();
        }

        ImGui.Separator();

        foreach (var item in GetParamItems())
        {
            var changed = item.Delta is not null;

            if (_changedOnly && !changed)
            {
                continue;
            }

            var searchText = $"{item.Name} {item.DisplayName}";
            if (!MatchesStateSearch(searchText, _paramSearch, changed, false, false))
            {
                continue;
            }

            if (changed)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text);
            }

            var label = string.Equals(item.DisplayName, item.Name, StringComparison.Ordinal)
                ? item.Name
                : $"{item.DisplayName}##semantic_param_{item.Name}";

            if (ImGui.Selectable(label, string.Equals(_selectedParamName, item.Name, StringComparison.Ordinal)))
            {
                _selectedParamName = item.Name;
                _rowSearch = "";
                _fieldSearch = "";
                SelectFirstVisibleRow();
            }

            if (changed)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered() && !string.Equals(item.DisplayName, item.Name, StringComparison.Ordinal))
            {
                ImGui.SetTooltip(item.Name);
            }
        }
    }

    private void RenderRowList()
    {
        ImGui.Text(_selectedParamName is null ? "Rows" : $"Rows — {_selectedParamName}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##semanticRowFilter",
            "Search ID/name, modified, unique, removed",
            ref _rowSearch,
            256);
        ImGui.Separator();

        var param = GetParamItems().FirstOrDefault(x => x.Name == _selectedParamName);
        if (param is null)
        {
            ImGui.TextDisabled("Select a PARAM.");
            return;
        }

        foreach (var row in GetRowItems(param))
        {
            if (_changedOnly && row.Delta is null)
            {
                continue;
            }

            var changed = row.Delta is not null;
            var added = row.Delta?.Kind == RegulationRowChangeKind.Added;
            var removed = row.Delta?.Kind == RegulationRowChangeKind.Removed;
            var rowName = row.DocumentRow?.Name ?? row.Delta?.ModName ?? row.Delta?.VanillaName ?? "(unnamed)";
            var labelText = $"{row.Id}  {rowName}";

            if (!MatchesStateSearch(labelText, _rowSearch, changed, added, removed))
            {
                continue;
            }

            if (changed)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text);
            }

            var prefix = added ? "[NEW] " : removed ? "[REMOVED] " : changed ? "[MODIFIED] " : "";
            var selected = _selectedRowId == row.Id && _selectedOccurrence == row.Occurrence;

            if (ImGui.Selectable(
                    $"{prefix}{labelText}##semantic_row_{row.Id}_{row.Occurrence}",
                    selected))
            {
                _selectedRowId = row.Id;
                _selectedOccurrence = row.Occurrence;
                _fieldSearch = "";
            }

            if (changed)
            {
                ImGui.PopStyleColor();
            }
        }
    }

    private void RenderFieldList()
    {
        ImGui.Text("Fields");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##semanticFieldFilter", "Search field or modified", ref _fieldSearch, 256);
        ImGui.Separator();

        var param = GetParamItems().FirstOrDefault(x => x.Name == _selectedParamName);
        if (param is null || _selectedRowId is null)
        {
            ImGui.TextDisabled("Select a row.");
            return;
        }

        var row = GetRowItems(param)
            .FirstOrDefault(x => x.Id == _selectedRowId.Value && x.Occurrence == _selectedOccurrence);

        if (row is null)
        {
            ImGui.TextDisabled("Select a row.");
            return;
        }

        if (row.Delta?.Kind == RegulationRowChangeKind.Added)
        {
            ImGui.TextColored(StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text, "NEW ROW — no vanilla counterpart");
            ImGui.Separator();
        }
        else if (row.Delta?.Kind == RegulationRowChangeKind.Removed)
        {
            ImGui.TextColored(StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text, "REMOVED ROW — present only in vanilla");
            ImGui.Separator();
        }
        else if (row.Delta is not null &&
                 !string.Equals(row.Delta.ModName, row.Delta.VanillaName, StringComparison.Ordinal))
        {
            ImGui.TextColored(
                StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text,
                $"Row name: {row.Delta.VanillaName ?? "(unnamed)"} → {row.Delta.ModName ?? "(unnamed)"}");
            ImGui.Separator();
        }

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("SemanticParamFields", 3, flags))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn(_index!.Document.SourceName, ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Vanilla", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableHeadersRow();

        foreach (var field in GetFieldItems(row))
        {
            var changed = field.Delta is not null;
            if (_changedOnly && !changed)
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(field.CommunityName)
                ? field.InternalName
                : $"{field.InternalName} ({field.CommunityName})";

            if (!MatchesStateSearch(displayName, _fieldSearch, changed, false, false))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);

            if (changed)
            {
                ImGui.TextColored(StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text, displayName);
            }
            else
            {
                ImGui.TextUnformatted(displayName);
            }

            if (!string.IsNullOrWhiteSpace(field.Description) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(field.Description);
            }

            ImGui.TableSetColumnIndex(1);
            RenderValue(field.ModValue, changed);

            ImGui.TableSetColumnIndex(2);
            RenderValue(field.VanillaValue, false);
        }

        ImGui.EndTable();
    }

    private IEnumerable<ParamItem> GetParamItems()
    {
        if (_index is null)
        {
            return [];
        }

        var documents = _index.Document.Params.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var deltas = _index.Delta.Params.ToDictionary(x => x.Name, StringComparer.Ordinal);

        return documents.Keys
            .Union(deltas.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                documents.TryGetValue(name, out var document);
                deltas.TryGetValue(name, out var delta);
                var display = document?.DisplayName ?? delta?.DisplayName ?? name;
                return new ParamItem(name, display, document, delta);
            });
    }

    private IEnumerable<RowItem> GetRowItems(ParamItem param)
    {
        var documentRows = param.Document?.Rows.ToDictionary(
            x => new RowKey(x.Id, x.Occurrence)) ?? new Dictionary<RowKey, RegulationRow>();
        var deltaRows = param.Delta?.Rows.ToDictionary(
            x => new RowKey(x.Id, x.Occurrence)) ?? new Dictionary<RowKey, RegulationRowDelta>();

        return documentRows.Keys
            .Union(deltaRows.Keys)
            .OrderBy(x => documentRows.TryGetValue(x, out var row) ? row.RowIndex : int.MaxValue)
            .ThenBy(x => x.Id)
            .ThenBy(x => x.Occurrence)
            .Select(key =>
            {
                documentRows.TryGetValue(key, out var document);
                deltaRows.TryGetValue(key, out var delta);
                return new RowItem(key.Id, key.Occurrence, document, delta);
            });
    }

    private IEnumerable<FieldItem> GetFieldItems(RowItem row)
    {
        var documentFields = row.DocumentRow?.Fields.ToDictionary(
            x => x.InternalName,
            StringComparer.Ordinal) ?? new Dictionary<string, RegulationField>();
        var deltaFields = row.Delta?.Fields.ToDictionary(
            x => x.InternalName,
            StringComparer.Ordinal) ?? new Dictionary<string, RegulationFieldDelta>();

        return documentFields.Keys
            .Union(deltaFields.Keys, StringComparer.Ordinal)
            .OrderBy(name => documentFields.TryGetValue(name, out var field) ? field.FieldIndex : int.MaxValue)
            .ThenBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                documentFields.TryGetValue(name, out var document);
                deltaFields.TryGetValue(name, out var delta);

                var modValue = delta?.ModValue ?? document?.Value;
                RegulationValue? vanillaValue;

                if (delta is not null)
                {
                    vanillaValue = delta.VanillaValue;
                }
                else
                {
                    vanillaValue = document?.Value;
                }

                return new FieldItem(
                    name,
                    document?.CommunityName ?? delta?.CommunityName,
                    document?.Description ?? delta?.Description,
                    modValue,
                    vanillaValue,
                    delta);
            });
    }

    private void SelectFirstVisibleRow()
    {
        var param = GetParamItems().FirstOrDefault(x => x.Name == _selectedParamName);
        if (param is null)
        {
            _selectedRowId = null;
            return;
        }

        var row = GetRowItems(param).FirstOrDefault(x => !_changedOnly || x.Delta is not null);
        _selectedRowId = row?.Id;
        _selectedOccurrence = row?.Occurrence ?? 0;
    }

    private void EnsureSelectionVisible()
    {
        if (_selectedParamName is null)
        {
            return;
        }

        var param = GetParamItems().FirstOrDefault(x => x.Name == _selectedParamName);
        if (param is null || (_changedOnly && param.Delta is null))
        {
            _selectedParamName = GetParamItems()
                .FirstOrDefault(x => !_changedOnly || x.Delta is not null)?.Name;
        }

        SelectFirstVisibleRow();
    }

    private static bool MatchesStateSearch(
        string text,
        string filter,
        bool modified,
        bool added,
        bool removed)
    {
        var input = filter.Trim();

        if (input.Equals("modified", StringComparison.OrdinalIgnoreCase))
        {
            return modified;
        }

        if (input.Equals("!modified", StringComparison.OrdinalIgnoreCase))
        {
            return !modified;
        }

        if (input.Equals("unique", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("added", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            return added;
        }

        if (input.Equals("removed", StringComparison.OrdinalIgnoreCase))
        {
            return removed;
        }

        return string.IsNullOrWhiteSpace(filter) ||
               text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void RenderValue(RegulationValue? value, bool changed)
    {
        if (value is null)
        {
            ImGui.TextDisabled("—");
            return;
        }

        if (changed)
        {
            ImGui.TextColored(StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text, value.Format());
        }
        else
        {
            ImGui.TextUnformatted(value.Format());
        }
    }

    private sealed record ParamItem(
        string Name,
        string DisplayName,
        RegulationParam? Document,
        RegulationParamDelta? Delta);

    private sealed record RowKey(int Id, int Occurrence);

    private sealed record RowItem(
        int Id,
        int Occurrence,
        RegulationRow? DocumentRow,
        RegulationRowDelta? Delta);

    private sealed record FieldItem(
        string InternalName,
        string? CommunityName,
        string? Description,
        RegulationValue? ModValue,
        RegulationValue? VanillaValue,
        RegulationFieldDelta? Delta);
}
