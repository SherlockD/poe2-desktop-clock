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
    private readonly CaptureOperationQueue _operations = new();

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

    public Task<CaptureResult> SaveSingleFrameAsync(
        nint windowHandle,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        SaveFrameAsync(windowHandle, outputPath, timeout, null, cancellationToken);

    public Task<CaptureResult> SaveRegionAsync(
        nint windowHandle,
        RegionDefinition region,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        SaveFrameAsync(windowHandle, outputPath, timeout, region, cancellationToken);

    private async Task<CaptureResult> SaveFrameAsync(
        nint windowHandle,
        string outputPath,
        TimeSpan timeout,
        RegionDefinition? region,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        return await _operations.RunAsync(
            token => SaveFrameCoreAsync(windowHandle, outputPath, region, token),
            timeoutCancellation.Token);
    }

    private async Task<CaptureResult> SaveFrameCoreAsync(
        nint windowHandle,
        string outputPath,
        RegionDefinition? region,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            using var frame = await frameReady.Task.WaitAsync(cancellationToken);
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
        _operations.Dispose();
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
