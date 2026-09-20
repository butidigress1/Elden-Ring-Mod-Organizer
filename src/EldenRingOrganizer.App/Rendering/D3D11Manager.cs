using Hexa.NET.GLFW;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Numerics;

namespace EldenRingOrganizer.Rendering;

public unsafe sealed class D3D11Manager : IDisposable
{
    private readonly DXGI _dxgi;
    private readonly D3D11 _d3d11;
    private ComPtr<IDXGIFactory2> _factory;
    private ComPtr<IDXGIAdapter1> _adapter;
    private ComPtr<IDXGISwapChain1> _swapChain;
    private SwapChainDesc1 _swapChainDesc;
    private ComPtr<ID3D11Texture2D> _backbuffer;
    private ComPtr<ID3D11RenderTargetView> _renderTargetView;

    internal ComPtr<ID3D11Device1> Device;
    internal ComPtr<ID3D11DeviceContext1> DeviceContext;

    public D3D11Manager(GLFWwindowPtr window)
    {
        _dxgi = DXGI.GetApi();
        _dxgi.CreateDXGIFactory2(0, out _factory);
        _adapter = GetHardwareAdapter();

        _d3d11 = D3D11.GetApi();
        D3DFeatureLevel[] levels = [D3DFeatureLevel.Level111, D3DFeatureLevel.Level110];
        ID3D11Device* baseDevice;
        ID3D11DeviceContext* baseContext;
        D3DFeatureLevel selectedLevel = 0;

        fixed (D3DFeatureLevel* pLevels = levels)
        {
            _d3d11.CreateDevice(
                (IDXGIAdapter*)_adapter.Handle,
                D3DDriverType.Unknown,
                0,
                (uint)CreateDeviceFlag.BgraSupport,
                pLevels,
                (uint)levels.Length,
                D3D11.SdkVersion,
                &baseDevice,
                &selectedLevel,
                &baseContext).ThrowHResult();
        }

        baseDevice->QueryInterface(out Device);
        baseContext->QueryInterface(out DeviceContext);
        baseDevice->Release();
        baseContext->Release();

        CreateSwapChain(window);
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Viewport Viewport { get; private set; }

    private void CreateSwapChain(GLFWwindow* window)
    {
        var width = 0;
        var height = 0;
        GLFW.GetWindowSize(window, &width, &height);
        var hwnd = GLFW.GetWin32Window(window);

        _swapChainDesc = new SwapChainDesc1
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            Format = Format.FormatB8G8R8A8Unorm,
            BufferCount = 2,
            BufferUsage = DXGI.UsageRenderTargetOutput,
            SampleDesc = new SampleDesc(1, 0),
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            Flags = 0
        };

        var fullscreenDesc = new SwapChainFullscreenDesc
        {
            Windowed = 1,
            RefreshRate = new Rational(0, 1),
            Scaling = ModeScaling.Unspecified,
            ScanlineOrdering = ModeScanlineOrder.Unspecified
        };

        _factory.CreateSwapChainForHwnd(
            (IUnknown*)Device.Handle,
            hwnd,
            &_swapChainDesc,
            &fullscreenDesc,
            null,
            &_swapChain.Handle);

        RecreateBackbuffer();
        Width = width;
        Height = height;
        Viewport = new Viewport(0, 0, Math.Max(1, width), Math.Max(1, height));
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DeviceContext.OMSetRenderTargets(0, null, null);
        _backbuffer.Release();
        _renderTargetView.Release();

        _swapChain.ResizeBuffers(_swapChainDesc.BufferCount, (uint)width, (uint)height, _swapChainDesc.Format, _swapChainDesc.Flags);
        Width = width;
        Height = height;
        Viewport = new Viewport(0, 0, width, height);
        RecreateBackbuffer();
    }

    public void Clear(Vector4 color)
    {
        DeviceContext.ClearRenderTargetView(_renderTargetView.Handle, (float*)&color);
    }

    public void SetTarget()
    {
        var renderTarget = _renderTargetView.Handle;
        DeviceContext.OMSetRenderTargets(1, &renderTarget, null);
        var viewport = Viewport;
        DeviceContext.RSSetViewports(1, &viewport);
    }

    public void Present()
    {
        _swapChain.Present(1, 0);
    }

    private void RecreateBackbuffer()
    {
        _swapChain.GetBuffer(0, out _backbuffer);
        ID3D11RenderTargetView* renderTarget;
        Device.CreateRenderTargetView((ID3D11Resource*)_backbuffer.Handle, null, &renderTarget);
        _renderTargetView.Handle = renderTarget;
    }

    private ComPtr<IDXGIAdapter1> GetHardwareAdapter()
    {
        ComPtr<IDXGIAdapter1> adapter = default;
        ComPtr<IDXGIFactory6> factory6;
        _factory.QueryInterface(out factory6);

        if (factory6.Handle != null)
        {
            for (uint index = 0;
                 (ResultCode)factory6.EnumAdapterByGpuPreference(index, GpuPreference.HighPerformance, out adapter) != ResultCode.DXGI_ERROR_NOT_FOUND;
                 index++)
            {
                AdapterDesc1 desc;
                adapter.GetDesc1(&desc);
                if (((AdapterFlag)desc.Flags & AdapterFlag.Software) == AdapterFlag.None)
                {
                    factory6.Release();
                    return adapter;
                }

                adapter.Release();
            }

            factory6.Release();
        }

        for (uint index = 0;
             (ResultCode)_factory.EnumAdapters1(index, &adapter.Handle) != ResultCode.DXGI_ERROR_NOT_FOUND;
             index++)
        {
            AdapterDesc1 desc;
            adapter.GetDesc1(&desc);
            if (((AdapterFlag)desc.Flags & AdapterFlag.Software) == AdapterFlag.None)
            {
                return adapter;
            }

            adapter.Release();
        }

        throw new InvalidOperationException("No Direct3D 11 hardware adapter was found.");
    }

    public void Dispose()
    {
        _renderTargetView.Release();
        _backbuffer.Release();
        _swapChain.Release();
        DeviceContext.Release();
        Device.Release();
        _d3d11.Dispose();
        _adapter.Release();
        _factory.Release();
        _dxgi.Dispose();
    }
}
