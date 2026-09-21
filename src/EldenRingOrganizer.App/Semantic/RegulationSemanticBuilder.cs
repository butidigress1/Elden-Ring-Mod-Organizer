using Andre.Formats;
using StudioCore.Editors.ParamEditor;

namespace EldenRingOrganizer.Semantic;

public static class RegulationSemanticBuilder
{
    public static RegulationIndex Build(
        ParamData data,
        string sourceName,
        string sourceRegulationPath,
        string vanillaRegulationPath)
    {
        var primaryBank = data.PrimaryBank;
        var vanillaBank = data.VanillaBank;
        var source = FileFingerprint.Create(sourceRegulationPath);
        var vanillaSource = FileFingerprint.Create(vanillaRegulationPath);

        return new RegulationIndex
        {
            CreatedAtUtc = DateTime.UtcNow,
            Document = BuildDocument(data, primaryBank, sourceName, source),
            Delta = BuildDelta(data, primaryBank, vanillaBank, source, vanillaSource)
        };
    }

    private static RegulationDocument BuildDocument(
        ParamData data,
        ParamBank bank,
        string sourceName,
        FileFingerprint source)
    {
        var parameters = new List<RegulationParam>(bank.Params.Count);

        foreach (var pair in bank.Params.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var paramName = pair.Key;
            var param = pair.Value;
            var displayName = GetParamDisplayName(data, paramName, param);
            var annotation = param.AppliedParamdef?.ParamType is string paramType
                ? data.GetParamAnnotations(paramType)
                : null;
            var rows = new List<RegulationRow>(param.Rows.Count);
            var occurrences = new Dictionary<int, int>();

            for (var rowIndex = 0; rowIndex < param.Rows.Count; rowIndex++)
            {
                var row = param.Rows[rowIndex];
                occurrences.TryGetValue(row.ID, out var occurrence);
                occurrences[row.ID] = occurrence + 1;

                var fields = new List<RegulationField>(param.Columns.Count);

                for (var fieldIndex = 0; fieldIndex < param.Columns.Count; fieldIndex++)
                {
                    var column = param.Columns[fieldIndex];
                    var value = column.GetValue(row);
                    var fieldAnnotation = annotation is null
                        ? null
                        : data.GetFieldAnnotation(annotation, column.Def.InternalName);
                    fields.Add(new RegulationField
                    {
                        InternalName = column.Def.InternalName,
                        CommunityName = fieldAnnotation?.Name,
                        Description = fieldAnnotation?.Description,
                        FieldType = column.ValueType.FullName ?? column.ValueType.Name,
                        FieldIndex = fieldIndex,
                        Value = RegulationValue.FromObject(value, column.ValueType)
                    });
                }

                rows.Add(new RegulationRow
                {
                    Id = row.ID,
                    Name = row.Name,
                    RowIndex = rowIndex,
                    Occurrence = occurrence,
                    Fields = fields
                });
            }

            parameters.Add(new RegulationParam
            {
                Name = paramName,
                DisplayName = displayName,
                ParamType = param.AppliedParamdef?.ParamType,
                Rows = rows
            });
        }

        return new RegulationDocument
        {
            SourceName = sourceName,
            Source = source,
            RegulationVersion = bank.ParamVersion,
            RegulationVersionDisplay = ParamUtils.ParseRegulationVersion(bank.ParamVersion),
            Params = parameters
        };
    }

    private static RegulationDelta BuildDelta(
        ParamData data,
        ParamBank primaryBank,
        ParamBank vanillaBank,
        FileFingerprint source,
        FileFingerprint vanillaSource)
    {
        var deltas = new List<RegulationParamDelta>();
        var paramNames = primaryBank.Params.Keys
            .Union(vanillaBank.Params.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);

        foreach (var paramName in paramNames)
        {
            primaryBank.Params.TryGetValue(paramName, out var primaryParam);
            vanillaBank.Params.TryGetValue(paramName, out var vanillaParam);

            var rowDeltas = BuildRowDeltas(data, primaryParam, vanillaParam);
            if (rowDeltas.Count == 0)
            {
                continue;
            }

            var metadataParam = primaryParam ?? vanillaParam!;
            deltas.Add(new RegulationParamDelta
            {
                Name = paramName,
                DisplayName = GetParamDisplayName(data, paramName, metadataParam),
                ParamType = primaryParam?.AppliedParamdef?.ParamType ?? vanillaParam?.AppliedParamdef?.ParamType,
                Rows = rowDeltas
            });
        }

        return new RegulationDelta
        {
            ModSource = source,
            VanillaSource = vanillaSource,
            ModRegulationVersion = primaryBank.ParamVersion,
            VanillaRegulationVersion = vanillaBank.ParamVersion,
            Params = deltas
        };
    }

    private static List<RegulationRowDelta> BuildRowDeltas(ParamData data, Param? primaryParam, Param? vanillaParam)
    {
        var deltas = new List<RegulationRowDelta>();
        var primaryGroups = GroupRows(primaryParam);
        var vanillaGroups = GroupRows(vanillaParam);
        var ids = primaryGroups.Keys.Union(vanillaGroups.Keys).OrderBy(x => x);

        foreach (var id in ids)
        {
            primaryGroups.TryGetValue(id, out var primaryRows);
            vanillaGroups.TryGetValue(id, out var vanillaRows);
            primaryRows ??= [];
            vanillaRows ??= [];

            var count = Math.Max(primaryRows.Count, vanillaRows.Count);

            for (var occurrence = 0; occurrence < count; occurrence++)
            {
                var primary = occurrence < primaryRows.Count ? primaryRows[occurrence] : null;
                var vanilla = occurrence < vanillaRows.Count ? vanillaRows[occurrence] : null;

                if (primary is null && vanilla is not null)
                {
                    deltas.Add(BuildRemovedRow(data, vanillaParam!, vanilla, occurrence));
                    continue;
                }

                if (primary is not null && vanilla is null)
                {
                    deltas.Add(BuildAddedRow(data, primaryParam!, primary, occurrence));
                    continue;
                }

                if (primary is null || vanilla is null)
                {
                    continue;
                }

                var fields = BuildFieldDeltas(data, primaryParam!, primary.Row, vanillaParam!, vanilla.Row);
                var nameChanged = !string.Equals(primary.Row.Name, vanilla.Row.Name, StringComparison.Ordinal);

                if (fields.Count == 0 && !nameChanged)
                {
                    continue;
                }

                deltas.Add(new RegulationRowDelta
                {
                    Id = id,
                    Occurrence = occurrence,
                    ModRowIndex = primary.Index,
                    VanillaRowIndex = vanilla.Index,
                    ModName = primary.Row.Name,
                    VanillaName = vanilla.Row.Name,
                    Kind = RegulationRowChangeKind.Modified,
                    Fields = fields
                });
            }
        }

        return deltas;
    }

    private static RegulationRowDelta BuildAddedRow(ParamData data, Param param, IndexedRow row, int occurrence)
    {
        return new RegulationRowDelta
        {
            Id = row.Row.ID,
            Occurrence = occurrence,
            ModRowIndex = row.Index,
            ModName = row.Row.Name,
            Kind = RegulationRowChangeKind.Added,
            Fields = param.Columns.Select(column => CreateFieldDelta(
                data,
                param,
                column,
                null,
                RegulationValue.FromObject(column.GetValue(row.Row), column.ValueType))).ToList()
        };
    }

    private static RegulationRowDelta BuildRemovedRow(ParamData data, Param param, IndexedRow row, int occurrence)
    {
        return new RegulationRowDelta
        {
            Id = row.Row.ID,
            Occurrence = occurrence,
            VanillaRowIndex = row.Index,
            VanillaName = row.Row.Name,
            Kind = RegulationRowChangeKind.Removed,
            Fields = param.Columns.Select(column => CreateFieldDelta(
                data,
                param,
                column,
                RegulationValue.FromObject(column.GetValue(row.Row), column.ValueType),
                null)).ToList()
        };
    }

    private static List<RegulationFieldDelta> BuildFieldDeltas(
        ParamData data,
        Param primaryParam,
        Param.Row primaryRow,
        Param vanillaParam,
        Param.Row vanillaRow)
    {
        var fields = new List<RegulationFieldDelta>();
        var primaryColumns = primaryParam.Columns.ToDictionary(x => x.Def.InternalName, StringComparer.Ordinal);
        var vanillaColumns = vanillaParam.Columns.ToDictionary(x => x.Def.InternalName, StringComparer.Ordinal);
        var names = primaryColumns.Keys.Union(vanillaColumns.Keys, StringComparer.Ordinal);

        foreach (var name in names)
        {
            primaryColumns.TryGetValue(name, out var primaryColumn);
            vanillaColumns.TryGetValue(name, out var vanillaColumn);

            var primaryValue = primaryColumn is null
                ? null
                : RegulationValue.FromObject(primaryColumn.GetValue(primaryRow), primaryColumn.ValueType);
            var vanillaValue = vanillaColumn is null
                ? null
                : RegulationValue.FromObject(vanillaColumn.GetValue(vanillaRow), vanillaColumn.ValueType);

            if (Equals(primaryValue, vanillaValue))
            {
                continue;
            }

            var metadataParam = primaryColumn is not null ? primaryParam : vanillaParam;
            var metadataColumn = primaryColumn ?? vanillaColumn!;
            fields.Add(CreateFieldDelta(data, metadataParam, metadataColumn, vanillaValue, primaryValue));
        }

        return fields;
    }


    private static RegulationFieldDelta CreateFieldDelta(
        ParamData data,
        Param param,
        Param.Column column,
        RegulationValue? vanillaValue,
        RegulationValue? modValue)
    {
        var annotation = param.AppliedParamdef?.ParamType is string paramType
            ? data.GetParamAnnotations(paramType)
            : null;
        var fieldAnnotation = annotation is null
            ? null
            : data.GetFieldAnnotation(annotation, column.Def.InternalName);

        return new RegulationFieldDelta
        {
            InternalName = column.Def.InternalName,
            CommunityName = fieldAnnotation?.Name,
            Description = fieldAnnotation?.Description,
            FieldType = column.ValueType.FullName ?? column.ValueType.Name,
            VanillaValue = vanillaValue,
            ModValue = modValue
        };
    }

    private static string GetParamDisplayName(ParamData data, string internalName, Param param)
    {
        if (param.AppliedParamdef is null)
        {
            return internalName;
        }

        var meta = data.GetParamMeta(param.AppliedParamdef);
        var match = meta?.DisplayNames.FirstOrDefault(x =>
            string.Equals(x.Param, internalName, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(match?.Name) ? internalName : match.Name;
    }

    private static Dictionary<int, List<IndexedRow>> GroupRows(Param? param)
    {
        var result = new Dictionary<int, List<IndexedRow>>();
        if (param is null)
        {
            return result;
        }

        for (var index = 0; index < param.Rows.Count; index++)
        {
            var row = param.Rows[index];
            if (!result.TryGetValue(row.ID, out var rows))
            {
                rows = [];
                result[row.ID] = rows;
            }

            rows.Add(new IndexedRow(index, row));
        }

        return result;
    }

    private sealed record IndexedRow(int Index, Param.Row Row);
}
