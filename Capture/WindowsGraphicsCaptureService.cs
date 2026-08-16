using System.Diagnostics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Foundation;
using Poe2DeskTracker.Regions;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;
using D3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Poe2DeskTracker.Capture;

public sealed class WindowsGraphicsCaptureService : IDisposable
{
    private readonly D3D11Device _device = null!;
    private readonly ID3D11DeviceContext _context = null!;

    public WindowsGraphicsCaptureService()
    {
        var result = D3D11.D3D11CreateDevice(
            null!,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            Array.Empty<FeatureLevel>(),
            out _device,
            out _context);

        result.CheckError();
    }

    public async Task<CaptureResult> SaveSingleFrameAsync(nint windowHandle, string outputPath, TimeSpan timeout)
    {
        return await SaveFrameAsync(windowHandle, outputPath, timeout, null);
    }

    public async Task<CaptureResult> SaveRegionAsync(nint windowHandle, RegionDefinition region, string outputPath, TimeSpan timeout)
    {
        return await SaveFrameAsync(windowHandle, outputPath, timeout, region);
    }

    private async Task<CaptureResult> SaveFrameAsync(nint windowHandle, string outputPath, TimeSpan timeout, RegionDefinition? region)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture is unavailable. Windows 10 version 1903 or newer is required.");
        }

        var item = GraphicsCaptureItemFactory.CreateForWindow(windowHandle);
        var size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException("The selected window has no capturable client area.");
        }

        var direct3DDevice = Direct3DDeviceFactory.CreateFrom(_device);
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            size);
        using var session = framePool.CreateCaptureSession(item);
        // The cursor is not part of the game's UI and its bright pixels can be
        // mistaken for a stack count when it rests over a currency slot.
        session.IsCursorCaptureEnabled = false;
        using var cancellationSource = new CancellationTokenSource(timeout);
        var frameReady = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Stopwatch.GetTimestamp();

        TypedEventHandler<Direct3D11CaptureFramePool, object> handler = (_, _) =>
        {
            var nextFrame = framePool.TryGetNextFrame();
            if (nextFrame is null)
            {
                return;
            }

            if (!frameReady.TrySetResult(nextFrame))
            {
                nextFrame.Dispose();
            }
        };

        framePool.FrameArrived += handler;

        try
        {
            session.StartCapture();
            using var frame = await frameReady.Task.WaitAsync(cancellationSource.Token);
            var contentSize = frame.ContentSize;
            var outputRegion = region is null
                ? PixelRegion.Full(contentSize.Width, contentSize.Height)
                : PixelRegion.FromNormalized(region, contentSize.Width, contentSize.Height);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            SaveFrameAsPng(frame.Surface, contentSize.Width, contentSize.Height, outputRegion, outputPath);
            return new CaptureResult(outputRegion.Width, outputRegion.Height, Stopwatch.GetElapsedTime(started));
        }
        finally
        {
            framePool.FrameArrived -= handler;
            direct3DDevice.Dispose();
        }
    }

    private void SaveFrameAsPng(IDirect3DSurface surface, int width, int height, PixelRegion outputRegion, string outputPath)
    {
        using var source = Direct3DTextureFactory.GetTexture(surface);
        var sourceDescription = source.Description;
        var stagingDescription = new Texture2DDescription
        {
            Width = checked((uint)width),
            Height = checked((uint)height),
            MipLevels = 1,
            ArraySize = 1,
            Format = sourceDescription.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        using var staging = _device.CreateTexture2D(stagingDescription);
        _context.CopyResource(staging, source);
        var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            var rowPitch = checked((int)mapped.RowPitch);
            var pixels = new byte[checked(outputRegion.Width * outputRegion.Height * 4)];
            for (var row = 0; row < outputRegion.Height; row++)
            {
                var sourceRowOffset = checked((outputRegion.Top + row) * rowPitch + outputRegion.Left * 4);
                var sourceRow = IntPtr.Add(mapped.DataPointer, sourceRowOffset);
                Marshal.Copy(sourceRow, pixels, row * outputRegion.Width * 4, outputRegion.Width * 4);
            }

            using var image = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(pixels, outputRegion.Width, outputRegion.Height);
            image.SaveAsPng(outputPath);
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _device.Dispose();
    }

    private readonly record struct PixelRegion(int Left, int Top, int Width, int Height)
    {
        internal static PixelRegion Full(int width, int height) => new(0, 0, width, height);

        internal static PixelRegion FromNormalized(RegionDefinition region, int sourceWidth, int sourceHeight)
        {
            var left = Math.Clamp((int)Math.Floor(region.X * sourceWidth), 0, sourceWidth - 1);
            var top = Math.Clamp((int)Math.Floor(region.Y * sourceHeight), 0, sourceHeight - 1);
            var right = Math.Clamp((int)Math.Ceiling((region.X + region.Width) * sourceWidth), left + 1, sourceWidth);
            var bottom = Math.Clamp((int)Math.Ceiling((region.Y + region.Height) * sourceHeight), top + 1, sourceHeight);
            return new PixelRegion(left, top, right - left, bottom - top);
        }
    }
}

public sealed record CaptureResult(int Width, int Height, TimeSpan Elapsed);

internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private const string GraphicsCaptureItemClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";

    internal static GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(
            GraphicsCaptureItemClassName,
            checked((uint)GraphicsCaptureItemClassName.Length),
            out var className));

        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(
                className,
                in GraphicsCaptureItemInteropGuid,
                out var factoryPointer));

            try
            {
                var itemPointer = CreateForWindow(factoryPointer, windowHandle, in GraphicsCaptureItemGuid);

                try
                {
                    return GraphicsCaptureItem.FromAbi(itemPointer);
                }
                finally
                {
                    Marshal.Release(itemPointer);
                }
            }
            finally
            {
                Marshal.Release(factoryPointer);
            }
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    private static nint CreateForWindow(nint factoryPointer, nint windowHandle, in Guid itemGuid)
    {
        var vtable = Marshal.ReadIntPtr(factoryPointer);
        var methodPointer = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(methodPointer);
        Marshal.ThrowExceptionForHR(createForWindow(factoryPointer, windowHandle, in itemGuid, out var itemPointer));
        return itemPointer;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowDelegate(nint factoryPointer, nint windowHandle, in Guid itemGuid, out nint itemPointer);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);
}

internal static class Direct3DDeviceFactory
{
    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    internal static IDirect3DDevice CreateFrom(D3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        var result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevice);
        Marshal.ThrowExceptionForHR(result);

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
