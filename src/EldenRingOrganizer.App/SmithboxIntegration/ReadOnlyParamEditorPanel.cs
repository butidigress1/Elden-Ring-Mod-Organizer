using Andre.Formats;
using Hexa.NET.ImGui;
using StudioCore.Application;
using StudioCore.Editors.ParamEditor;
using System.Globalization;
using System.Numerics;

namespace EldenRingOrganizer.SmithboxIntegration;

public sealed class ReadOnlyParamEditorPanel
{
    private SmithboxParamSession? _session;
    private string _paramSearch = "";
    private string _rowSearch = "";
    private string _fieldSearch = "";
    private bool _paramChangedOnly;
    private bool _fieldChangedOnly;
    private ComparisonFilter _rowFilter = ComparisonFilter.All;
    private string? _selectedParamName;
    private Param.Row? _selectedRow;

    public void SetSession(SmithboxParamSession? session)
    {
        _session = session;
        _paramSearch = "";
        _rowSearch = "";
        _fieldSearch = "";
        _paramChangedOnly = false;
        _fieldChangedOnly = false;
        _rowFilter = ComparisonFilter.All;
        _selectedParamName = null;
        _selectedRow = null;

        if (session is null)
        {
            return;
        }

        _selectedParamName = session.PrimaryBank.Params.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        SelectFirstRow();
    }

    public void Render()
    {
        if (_session is null)
        {
            ImGui.TextDisabled("No PARAM source is loaded.");
            return;
        }

        ImGui.Text($"Selected Source: {_session.SourceName}");
        ImGui.SameLine();
        ImGui.TextDisabled("  |  Baseline: Vanilla Game Data  |  Read-only");
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        var paramsWidth = Math.Clamp(available.X * 0.22f, 220f, 360f);
        var rowsWidth = Math.Clamp(available.X * 0.28f, 280f, 460f);

        ImGui.BeginChild("ParamBrowser", new Vector2(paramsWidth, available.Y), ImGuiChildFlags.Borders);
        RenderParamList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("RowBrowser", new Vector2(rowsWidth, available.Y), ImGuiChildFlags.Borders);
        RenderRowList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("FieldBrowser", new Vector2(0, available.Y), ImGuiChildFlags.Borders);
        RenderFieldList();
        ImGui.EndChild();
    }

    private void RenderParamList()
    {
        ImGui.Text("PARAMs");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##paramFilter", "Search internal or community name", ref _paramSearch, 256);
        ImGui.Checkbox("Changed only##paramChangedOnly", ref _paramChangedOnly);
        ImGui.Separator();

        foreach (var pair in _session!.PrimaryBank.Params.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var internalName = pair.Key;
            var param = pair.Value;
            var displayName = GetParamDisplayName(internalName, param);
            var searchText = $"{internalName} {displayName}";

            if (!Matches(searchText, _paramSearch))
            {
                continue;
            }

            var changed = _session.PrimaryBank.VanillaDiffCache.TryGetValue(internalName, out var changedRows)
                && changedRows.Count > 0;

            if (_paramChangedOnly && !changed)
            {
                continue;
            }

            if (changed)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text);
            }

            var label = displayName == internalName
                ? internalName
                : $"{displayName}##{internalName}";

            if (ImGui.Selectable(label, string.Equals(_selectedParamName, internalName, StringComparison.Ordinal)))
            {
                _selectedParamName = internalName;
                _rowSearch = "";
                _fieldSearch = "";
                SelectFirstRow();
            }

            if (changed)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered() && displayName != internalName)
            {
                ImGui.SetTooltip(internalName);
            }
        }
    }

    private void RenderRowList()
    {
        ImGui.Text(_selectedParamName is null ? "Rows" : $"Rows — {_selectedParamName}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##rowFilter", "Search ID or row name", ref _rowSearch, 256);
        RenderComparisonFilter();
        ImGui.Separator();

        if (_selectedParamName is null || !_session!.PrimaryBank.Params.TryGetValue(_selectedParamName, out var param))
        {
            ImGui.TextDisabled("Select a PARAM.");
            return;
        }

        _session.PrimaryBank.VanillaDiffCache.TryGetValue(_selectedParamName, out var diffRows);
        _session.VanillaBank.Params.TryGetValue(_selectedParamName, out var vanillaParam);

        for (var i = 0; i < param.Rows.Count; i++)
        {
            var row = param.Rows[i];
            var rowName = string.IsNullOrWhiteSpace(row.Name) ? "(unnamed)" : row.Name;
            var labelText = $"{row.ID}  {rowName}";

            if (!Matches(labelText, _rowSearch))
            {
                continue;
            }

            var vanillaRow = vanillaParam is null ? null : FindMatchingVanillaRow(param, vanillaParam, row);
            var state = vanillaRow is null
                ? ComparisonState.Added
                : diffRows?.Contains(row) == true
                    ? ComparisonState.Modified
                    : ComparisonState.Unchanged;

            if (!_rowFilter.Matches(state))
            {
                continue;
            }

            var changed = state is not ComparisonState.Unchanged;
            if (changed)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text);
            }

            var prefix = state is ComparisonState.Added ? "[NEW] " : "";
            if (ImGui.Selectable($"{prefix}{labelText}##row_{i}", ReferenceEquals(_selectedRow, row)))
            {
                _selectedRow = row;
                _fieldSearch = "";
            }

            if (changed)
            {
                ImGui.PopStyleColor();
            }

            if (state is ComparisonState.Added && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("This row exists in the selected mod but not in Vanilla Game Data.");
            }
        }
    }

    private void RenderFieldList()
    {
        ImGui.Text("Fields");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##fieldFilter", "Search internal or community field name", ref _fieldSearch, 256);
        ImGui.Checkbox("Changed only##fieldChangedOnly", ref _fieldChangedOnly);
        ImGui.Separator();

        if (_selectedParamName is null ||
            _selectedRow is null ||
            !_session!.PrimaryBank.Params.TryGetValue(_selectedParamName, out var primaryParam))
        {
            ImGui.TextDisabled("Select a row.");
            return;
        }

        _session.VanillaBank.Params.TryGetValue(_selectedParamName, out var vanillaParam);
        var vanillaRow = vanillaParam is null ? null : FindMatchingVanillaRow(primaryParam, vanillaParam, _selectedRow);
        var rowIsAdded = vanillaRow is null;

        if (rowIsAdded)
        {
            ImGui.TextColored(
                StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text,
                "NEW ROW — no vanilla counterpart");
            ImGui.Separator();
        }

        var annotation = primaryParam.AppliedParamdef?.ParamType is string paramType
            ? _session.Data.GetParamAnnotations(paramType)
            : null;

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("ReadonlyParamFields", 3, flags))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn(_session.SourceName, ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Vanilla", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableHeadersRow();

        foreach (var column in primaryParam.Columns)
        {
            var internalName = column.Def.InternalName;
            var fieldAnnotation = annotation is null ? null : _session.Data.GetFieldAnnotation(annotation, internalName);
            var communityName = fieldAnnotation?.Name;
            var displayName = string.IsNullOrWhiteSpace(communityName)
                ? internalName
                : $"{internalName} ({communityName})";

            if (!Matches($"{internalName} {communityName}", _fieldSearch))
            {
                continue;
            }

            var primaryValue = column.GetValue(_selectedRow);
            object? vanillaValue = null;

            if (vanillaParam is not null && vanillaRow is not null)
            {
                var vanillaColumn = vanillaParam[internalName];
                if (vanillaColumn is not null)
                {
                    vanillaValue = vanillaColumn.GetValue(vanillaRow);
                }
            }

            var changed = rowIsAdded || ValuesDiffer(primaryValue, vanillaValue, column.ValueType);

            if (_fieldChangedOnly && !changed)
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(displayName);

            if (!string.IsNullOrWhiteSpace(fieldAnnotation?.Description) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(fieldAnnotation.Description);
            }

            ImGui.TableSetColumnIndex(1);
            if (changed && CFG.Current.ParamEditor_Field_List_Display_Modified_Field_Bg)
            {
                ImGui.TableSetBgColor(
                    ImGuiTableBgTarget.CellBg,
                    ImGui.ColorConvertFloat4ToU32(StudioCore.Application.UI.Current.ParamDiffBackgroundColor));
                ImGui.TextUnformatted(FormatValue(primaryValue));
            }
            else if (changed)
            {
                ImGui.TextColored(StudioCore.Application.UI.Current.ImGui_PrimaryChanged_Text, FormatValue(primaryValue));
            }
            else
            {
                ImGui.TextUnformatted(FormatValue(primaryValue));
            }

            ImGui.TableSetColumnIndex(2);
            if (vanillaValue is null)
            {
                ImGui.TextDisabled("—");
            }
            else
            {
                ImGui.TextUnformatted(FormatValue(vanillaValue));
            }
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
        if (ImGui.Selectable($"{label}##rowState_{label}", _rowFilter == filter))
        {
            _rowFilter = filter;
            if (_selectedRow is not null)
            {
                EnsureSelectedRowVisible();
            }
        }
    }

    private void EnsureSelectedRowVisible()
    {
        if (_session is null ||
            _selectedParamName is null ||
            !_session.PrimaryBank.Params.TryGetValue(_selectedParamName, out var primaryParam) ||
            !_session.VanillaBank.Params.TryGetValue(_selectedParamName, out var vanillaParam))
        {
            return;
        }

        _session.PrimaryBank.VanillaDiffCache.TryGetValue(_selectedParamName, out var diffRows);

        if (_selectedRow is not null)
        {
            var vanillaRow = FindMatchingVanillaRow(primaryParam, vanillaParam, _selectedRow);
            var state = vanillaRow is null
                ? ComparisonState.Added
                : diffRows?.Contains(_selectedRow) == true
                    ? ComparisonState.Modified
                    : ComparisonState.Unchanged;

            if (_rowFilter.Matches(state))
            {
                return;
            }
        }

        _selectedRow = primaryParam.Rows.FirstOrDefault(row =>
        {
            var vanillaRow = FindMatchingVanillaRow(primaryParam, vanillaParam, row);
            var state = vanillaRow is null
                ? ComparisonState.Added
                : diffRows?.Contains(row) == true
                    ? ComparisonState.Modified
                    : ComparisonState.Unchanged;
            return _rowFilter.Matches(state);
        });
    }

    private string GetParamDisplayName(string internalName, Param param)
    {
        if (param.AppliedParamdef is null)
        {
            return internalName;
        }

        var meta = _session!.Data.GetParamMeta(param.AppliedParamdef);
        var match = meta?.DisplayNames.FirstOrDefault(x => string.Equals(x.Param, internalName, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(match?.Name) ? internalName : match.Name;
    }

    private void SelectFirstRow()
    {
        _selectedRow = null;

        if (_session is null ||
            _selectedParamName is null ||
            !_session.PrimaryBank.Params.TryGetValue(_selectedParamName, out var param) ||
            param.Rows.Count == 0)
        {
            return;
        }

        _selectedRow = param.Rows[0];
    }

    private static Param.Row? FindMatchingVanillaRow(Param primaryParam, Param vanillaParam, Param.Row selectedRow)
    {
        var occurrence = 0;

        foreach (var row in primaryParam.Rows)
        {
            if (ReferenceEquals(row, selectedRow))
            {
                break;
            }

            if (row.ID == selectedRow.ID)
            {
                occurrence++;
            }
        }

        var vanillaOccurrence = 0;
        foreach (var row in vanillaParam.Rows)
        {
            if (row.ID != selectedRow.ID)
            {
                continue;
            }

            if (vanillaOccurrence == occurrence)
            {
                return row;
            }

            vanillaOccurrence++;
        }

        return null;
    }

    private static bool ValuesDiffer(object? selected, object? vanilla, Type type)
    {
        if (selected is null || vanilla is null)
        {
            return selected is not null || vanilla is not null;
        }

        var selectedValue = selected;
        var vanillaValue = vanilla;
        return ParamUtils.IsValueDiff(ref selectedValue, ref vanillaValue, type);
    }

    private static bool Matches(string text, string filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
               text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "—",
            byte[] bytes => bytes.Length <= 32
                ? Convert.ToHexString(bytes)
                : $"{Convert.ToHexString(bytes.AsSpan(0, 32))}… ({bytes.Length} bytes)",
            float number => number.ToString("G9", CultureInfo.InvariantCulture),
            double number => number.ToString("G17", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
    }
}
