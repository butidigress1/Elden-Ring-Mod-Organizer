using EldenRingOrganizer.Semantic;
using StudioCore.Application;
using StudioCore.Editors.ParamEditor;

namespace EldenRingOrganizer.SmithboxIntegration;

public static class SmithboxRegulationIndexBuilder
{
    public static async Task<RegulationIndex> BuildAsync(
        string gameFolder,
        string modRoot,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SmithboxRuntime.EnsureReady();

        var sourceRegulationPath = Path.Combine(modRoot, "regulation.bin");
        var vanillaRegulationPath = Path.Combine(gameFolder, "regulation.bin");

        if (!File.Exists(sourceRegulationPath))
        {
            throw new FileNotFoundException("Installed mod regulation.bin was not found.", sourceRegulationPath);
        }

        if (!File.Exists(vanillaRegulationPath))
        {
            throw new FileNotFoundException("Vanilla regulation.bin was not found.", vanillaRegulationPath);
        }

        var descriptor = new ProjectDescriptor
        {
            ProjectGUID = Guid.NewGuid(),
            ProjectName = sourceName,
            ProjectPath = modRoot,
            DataPath = gameFolder,
            ProjectType = ProjectType.ER,
            EnableParamEditor = true,
            EnableTextEditor = false,
            ImportedParamRowNames = true
        };

        var project = new ProjectEntry
        {
            Descriptor = descriptor
        };

        try
        {
            project.SetupDLLs();
            cancellationToken.ThrowIfCancellationRequested();

            project.VFS = new ProjectVFS(project);
            project.Locator = new ProjectFileLocator(project);
            project.Handler = new ProjectEditorHandler(project);

            project.VFS.Initialize();
            await project.Locator.Initialize(_ => { }, true);

            cancellationToken.ThrowIfCancellationRequested();

            project.Handler.ParamData = new ParamData(project);
            var loaded = await project.Handler.ParamData.Setup();

            if (!loaded)
            {
                throw new InvalidOperationException("Smithbox could not load PARAM data for semantic indexing.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await RowNameHelper.ImportRowNamesTask(
                project,
                project.Handler.ParamData.PrimaryBank,
                CFG.Current.ParamEditor_Import_Language);

            await RowNameHelper.ImportRowNamesTask(
                project,
                project.Handler.ParamData.VanillaBank,
                CFG.Current.ParamEditor_Import_Language);

            var primary = project.Handler.ParamData.PrimaryBank;
            var vanilla = project.Handler.ParamData.VanillaBank;

            if (primary.Params.Count == 0)
            {
                throw new InvalidOperationException("Semantic indexer loaded no PARAMs from the installed mod.");
            }

            if (vanilla.Params.Count == 0)
            {
                throw new InvalidOperationException("Semantic indexer loaded no PARAMs from vanilla.");
            }

            return RegulationSemanticBuilder.Build(
                project.Handler.ParamData,
                sourceName,
                sourceRegulationPath,
                vanillaRegulationPath);
        }
        finally
        {
            project.Dispose();
        }
    }
}
