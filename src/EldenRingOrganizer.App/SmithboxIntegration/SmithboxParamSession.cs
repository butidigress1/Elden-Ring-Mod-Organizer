using StudioCore;
using StudioCore.Application;
using StudioCore.Editors.ParamEditor;
using StudioCore.Editors.TextEditor;

namespace EldenRingOrganizer.SmithboxIntegration;

public sealed class SmithboxParamSession : IDisposable
{
    private SmithboxParamSession(
        ProjectEntry project,
        string sourceName,
        string projectPath,
        string? sourceRegulationPath)
    {
        Project = project;
        SourceName = sourceName;
        ProjectPath = projectPath;
        SourceRegulationPath = sourceRegulationPath;
    }

    public ProjectEntry Project { get; }
    public ParamData Data => Project.Handler.ParamData;
    public ParamBank PrimaryBank => Data.PrimaryBank;
    public ParamBank VanillaBank => Data.VanillaBank;
    public TextData TextData => Project.Handler.TextData;
    public TextEditorView TextView => Project.Handler.TextEditor.ViewHandler.ActiveView;
    public string SourceName { get; }
    public string ProjectPath { get; }
    public string? SourceRegulationPath { get; }

    public static Task<SmithboxParamSession> LoadAsync(
        string gameFolder,
        string projectPath,
        string sourceName,
        string? sourceRegulationPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SmithboxRuntime.EnsureReady();

            Directory.CreateDirectory(projectPath);

            var descriptor = new ProjectDescriptor
            {
                ProjectGUID = Guid.NewGuid(),
                ProjectName = sourceName,
                ProjectPath = projectPath,
                DataPath = gameFolder,
                ProjectType = ProjectType.ER,
                EnableParamEditor = true,
                EnableTextEditor = true,
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
                var paramsLoaded = await project.Handler.ParamData.Setup();

                if (!paramsLoaded)
                {
                    throw new InvalidOperationException("Smithbox ParamData setup did not complete successfully.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                project.Handler.TextData = new TextData(project);
                var textLoaded = await project.Handler.TextData.Setup();

                if (!textLoaded)
                {
                    throw new InvalidOperationException("Smithbox TextData setup did not complete successfully.");
                }

                project.Handler.TextEditor = new TextEditorScreen(project);

                cancellationToken.ThrowIfCancellationRequested();

                await RowNameHelper.ImportRowNamesTask(
                    project,
                    project.Handler.ParamData.PrimaryBank,
                    CFG.Current.ParamEditor_Import_Language);

                await RowNameHelper.ImportRowNamesTask(
                    project,
                    project.Handler.ParamData.VanillaBank,
                    CFG.Current.ParamEditor_Import_Language);

                project.Handler.ParamData.RefreshAllParamDiffCaches(false);

                if (project.Handler.ParamData.PrimaryBank.Params.Count == 0)
                {
                    throw new InvalidOperationException("Smithbox loaded no PARAMs from the selected source.");
                }

                if (project.Handler.ParamData.VanillaBank.Params.Count == 0)
                {
                    throw new InvalidOperationException("Smithbox loaded no PARAMs from the vanilla baseline.");
                }

                if (project.Handler.TextData.PrimaryBank.Containers.Count == 0)
                {
                    throw new InvalidOperationException("Smithbox loaded no FMG text containers from the selected source.");
                }

                if (project.Handler.TextData.VanillaBank.Containers.Count == 0)
                {
                    throw new InvalidOperationException("Smithbox loaded no FMG text containers from the vanilla baseline.");
                }

                return new SmithboxParamSession(project, sourceName, projectPath, sourceRegulationPath);
            }
            catch
            {
                project.Dispose();
                throw;
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        Project.Dispose();
    }
}
