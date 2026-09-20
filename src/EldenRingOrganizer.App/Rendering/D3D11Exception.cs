using System.Runtime.InteropServices;

namespace EldenRingOrganizer.Rendering;

public sealed class D3D11Exception : Exception
{
    public D3D11Exception(ResultCode code)
        : base((Marshal.GetExceptionForHR((int)code) ?? new Exception("Direct3D 11 operation failed.")).Message)
    {
    }
}
