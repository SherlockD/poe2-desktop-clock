using System.Runtime.InteropServices;
using Vortice.DXGI;
using WinRT;
using Windows.Graphics.DirectX.Direct3D11;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;

namespace Poe2DeskTracker.Capture;

internal static class Direct3DDeviceFactory
{
    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    internal static IDirect3DDevice CreateFrom(D3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevice));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
        }
        finally
        {
            Marshal.Release(graphicsDevice);
        }
    }
}
