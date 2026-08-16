using System.Runtime.InteropServices;
using WinRT;
using Windows.Graphics.Capture;

namespace Poe2DeskTracker.Capture;

internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private const string GraphicsCaptureItemClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";

    internal static GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(GraphicsCaptureItemClassName, checked((uint)GraphicsCaptureItemClassName.Length), out var className));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, in GraphicsCaptureItemInteropGuid, out var factoryPointer));
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
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);
}
