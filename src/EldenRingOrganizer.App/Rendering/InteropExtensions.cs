namespace EldenRingOrganizer.Rendering;

public static class InteropExtensions
{
    public static void ThrowHResult(this int code)
    {
        if (code < 0)
        {
            throw new D3D11Exception((ResultCode)code);
        }
    }
}
