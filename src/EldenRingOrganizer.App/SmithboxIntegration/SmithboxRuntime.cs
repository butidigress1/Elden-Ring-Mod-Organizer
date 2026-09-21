using Microsoft.Extensions.Logging;
using SoulsFormats;
using StudioCore;
using StudioCore.Application;
using StudioCore.Editors.ParamEditor;
using StudioCore.Editors.TextEditor;
using StudioCore.Interface;

namespace EldenRingOrganizer.SmithboxIntegration;

public static class SmithboxRuntime
{
    private static readonly object RuntimeLock = new();
    private static bool _ready;
    private static ILoggerFactory? _loggerFactory;

    public static void EnsureReady()
    {
        lock (RuntimeLock)
        {
            if (_ready)
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
            CFG.Current.TextEditor_Text_File_List_Grouped_Display = true;
            CFG.Current.TextEditor_Text_Entry_Enable_Grouped_Entries = true;

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

            _ready = true;
        }
    }
}
