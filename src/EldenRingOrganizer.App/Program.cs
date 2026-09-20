using EldenRingOrganizer.Rendering;
using EldenRingOrganizer.UI;
using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D11;
using Hexa.NET.ImGui.Backends.GLFW;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using BackendGlfwWindowPtr = Hexa.NET.ImGui.Backends.GLFW.GLFWwindowPtr;
using GlfwWindowPtr = Hexa.NET.GLFW.GLFWwindowPtr;

namespace EldenRingOrganizer;

public static class Program
{
    [STAThread]
    public static unsafe void Main()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Elden Ring Organizer failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static unsafe void Run()
    {
        if (GLFW.Init() == 0)
        {
            throw new InvalidOperationException("GLFW initialization failed.");
        }

        GLFW.WindowHint(GLFW.GLFW_CLIENT_API, GLFW.GLFW_NO_API);
        GLFW.WindowHint(GLFW.GLFW_FOCUSED, 1);
        GLFW.WindowHint(GLFW.GLFW_RESIZABLE, 1);

        GlfwWindowPtr window = GLFW.CreateWindow(1280, 800, "Elden Ring Organizer — 0.1C C# Rewrite", null, null);
        if (window.IsNull)
        {
            GLFW.Terminate();
            throw new InvalidOperationException("Window creation failed.");
        }

        try
        {
            using var graphics = new D3D11Manager(window);
            var context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            ImGuiImplGLFW.SetCurrentContext(context);
            if (!ImGuiImplGLFW.InitForOther(Unsafe.BitCast<GlfwWindowPtr, BackendGlfwWindowPtr>(window), true))
            {
                throw new InvalidOperationException("ImGui GLFW backend initialization failed.");
            }

            ImGuiImplD3D11.SetCurrentContext(context);
            if (!ImGuiImplD3D11.Init(
                    Unsafe.BitCast<ComPtr<ID3D11Device1>, ID3D11DevicePtr>(graphics.Device),
                    Unsafe.BitCast<ComPtr<ID3D11DeviceContext1>, ID3D11DeviceContextPtr>(graphics.DeviceContext)))
            {
                throw new InvalidOperationException("ImGui D3D11 backend initialization failed.");
            }

            using var shell = new OrganizerShell(AppContext.BaseDirectory);
            shell.ApplyStyle();

            GLFW.SetFramebufferSizeCallback(window, Resized);
            void Resized(Hexa.NET.GLFW.GLFWwindow* _, int width, int height)
            {
                graphics.Resize(width, height);
            }

            var clear = new Vector4(0.075f, 0.078f, 0.086f, 1f);
            while (GLFW.WindowShouldClose(window) == 0)
            {
                GLFW.PollEvents();

                ImGuiImplD3D11.NewFrame();
                ImGuiImplGLFW.NewFrame();
                ImGui.NewFrame();

                shell.Render();

                ImGui.Render();
                ImGui.EndFrame();

                graphics.Clear(clear);
                graphics.SetTarget();
                ImGuiImplD3D11.RenderDrawData(ImGui.GetDrawData());
                graphics.Present();
            }

            ImGuiImplD3D11.Shutdown();
            ImGuiImplD3D11.SetCurrentContext(null);
            ImGuiImplGLFW.Shutdown();
            ImGuiImplGLFW.SetCurrentContext(null);
            ImGui.DestroyContext();
        }
        finally
        {
            GLFW.DestroyWindow(window);
            GLFW.Terminate();
        }
    }
}
