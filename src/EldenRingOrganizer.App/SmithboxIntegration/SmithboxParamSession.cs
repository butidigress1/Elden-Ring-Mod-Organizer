using Microsoft.Extensions.Logging;
using SoulsFormats;
using StudioCore;
using StudioCore.Application;
using StudioCore.Editors.ParamEditor;
using StudioCore.Interface;

namespace EldenRingOrganizer.SmithboxIntegration;

public sealed class SmithboxParamSession : IDisposable
{
    private static readonly object RuntimeLock = new();
    private static bool _runtimeReady;
    private static ILoggerFactory? _loggerFactory;

    private SmithboxParamSession(ProjectEntry project, string sourceName, string? sourceRegulationPath)
    {
        Project = project;
        SourceName = sourceName;
        SourceRegulationPath = sourceRegulationPath;
    }

    public ProjectEntry Project { get; }
    public ParamData Data => Project.Handler.ParamData;
    public ParamBank PrimaryBank => Data.PrimaryBank;
    public ParamBank VanillaBank => Data.VanillaBank;
    public string SourceName { get; }
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
                project.Handler = new ProjectEditorHandler(project);
                project.VFS.Initialize();

                cancellationToken.ThrowIfCancellationRequested();

                project.Handler.ParamData = new ParamData(project);
                var loaded = await project.Handler.ParamData.Setup();

                if (!loaded)
                {
                    throw new InvalidOperationException("Smithbox ParamData setup did not complete successfully.");
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

                project.Handler.ParamData.RefreshAllParamDiffCaches(false);

                if (project.Handler.ParamData.PrimaryBank.Params.Count == 0)
                {
                    throw new InvalidOperationException("Smithbox loaded no PARAMs from the selected source.");
                }

                if (project.Handler.ParamData.VanillaBank.Params.Count == 0)
                {
                    throw new InvalidOperationException("Smithbox loaded no PARAMs from the vanilla baseline.");
                }

                return new SmithboxParamSession(project, sourceName, sourceRegulationPath);
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
            UI.Setup();

            CFG.Current.ParamEditor_Import_Language = "English";
            CFG.Current.ParamEditor_Annotation_Language = "English";
            CFG.Current.Param_ShowVanillaColumn = true;
            CFG.Current.Param_ShowAuxColumn = false;
            CFG.Current.ParamEditor_Param_List_Display_Community_Names = true;
            CFG.Current.ParamEditor_FieldNameMode = ParamFieldNameMode.Source_Community;
            CFG.Current.ParamEditor_Field_List_Display_Modified_Field_Bg = true;
            CFG.Current.ParamEditor_Row_List_Display_Modified_Row_Bg = true;
            CFG.Current.Project_VFS_Prefer_Loose_Files = false;

            LOC.Setup();
            LOC.Load();

            _loggerFactory = LoggerFactory.Create(_ => { });
            Smithbox.SbLoggerFactory = _loggerFactory;
            Smithbox.SbLogger = _loggerFactory.CreateLogger<Smithbox>();
            Util.Logging.LoggerFactory = _loggerFactory;
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
