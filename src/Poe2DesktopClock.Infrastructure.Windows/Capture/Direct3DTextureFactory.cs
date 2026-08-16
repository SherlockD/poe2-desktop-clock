using System.Runtime.InteropServices;
using WinRT;
using Windows.Graphics.DirectX.Direct3D11;
using D3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Poe2DeskTracker.Capture;

internal static class Direct3DTextureFactory
{
    internal static D3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var textureGuid = typeof(D3D11Texture2D).GUID;
        access.GetInterface(in textureGuid, out var texturePointer);
        return new D3D11Texture2D(texturePointer);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface(in Guid iid, out nint result);
    }
}
