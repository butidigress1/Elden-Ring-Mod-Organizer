using Microsoft.Extensions.Logging;
using SoulsFormats;
using StudioCore;
using StudioCore.Application;
using StudioCore.Editors.ParamEditor;
using StudioCore.Editors.TextEditor;
using StudioCore.Interface;

namespace EldenRingOrganizer.SmithboxIntegration;

public sealed class SmithboxParamSession : IDisposable
{
    private static readonly object RuntimeLock = new();
    private static bool _runtimeReady;
    private static ILoggerFactory? _loggerFactory;

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
            EnsureRuntime();

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

    private static void EnsureRuntime()
    {
        lock (RuntimeLock)
        {
            if (_runtimeReady)
            {
                return;
            }

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            Startup.Setup();
            CFG.Setup();
            StudioCore.Application.UI.Setup();

            CFG.Current.ParamEditor_Import_Language = "English";
            CFG.Current.ParamEditor_Annotation_Language = "English";
            CFG.Current.Param_ShowVanillaColumn = true;
            CFG.Current.Param_ShowAuxColumn = false;
            CFG.Current.ParamEditor_Param_List_Display_Community_Names = true;
            CFG.Current.ParamEditor_FieldNameMode = ParamFieldNameMode.Source_Community;
            CFG.Current.ParamEditor_Field_List_Display_Modified_Field_Bg = true;
            CFG.Current.ParamEditor_Row_List_Display_Modified_Row_Bg = true;
            CFG.Current.Project_VFS_Prefer_Loose_Files = false;
            CFG.Current.TextEditor_Primary_Category = TextContainerCategory.English;
            CFG.Current.TextEditor_Include_Vanilla_Cache = true;

            LOC.Setup();
            LOC.Load();

            _loggerFactory = LoggerFactory.Create(_ => { });
            Smithbox.SbLoggerFactory = _loggerFactory;
            Smithbox.SbLogger = _loggerFactory.CreateLogger<Smithbox>();
            SoulsFormats.Util.Logging.LoggerFactory = _loggerFactory;
            Andre.Core.AndreLogging.LoggerFactory = _loggerFactory;

            BinaryReaderEx.CurrentProjectType = "ER";
            BinaryReaderEx.IgnoreAsserts = CFG.Current.System_Ignore_Read_Asserts;
            BinaryReaderEx.UseDCXHeuristicOnReadFailure = CFG.Current.System_Apply_DCX_Heuristic;

            _runtimeReady = true;
        }
    }

    public void Dispose()
    {
        Project.Dispose();
    }
}
