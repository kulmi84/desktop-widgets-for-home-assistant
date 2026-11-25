using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

[assembly: SupportedOSPlatform("windows10.0.19041")]

static class Program
{
    static readonly Guid ProviderId = new("f110e6ce-7bd0-475b-9616-8fcb5a85dfcd");

    static void Main()
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0));
        try
        {
            ComWrappersSupport.InitializeComWrappers();
            var factory = new WidgetFactory();
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(in ProviderId, factory, 4, 1, out uint cookie));
            try { Thread.Sleep(Timeout.Infinite); }
            finally
            {
                CoRevokeClassObject(cookie);
                GC.KeepAlive(factory);
            }
        }
        finally { CoUninitialize(); }
    }


    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr reserved, uint flags);

    [DllImport("ole32.dll")]
    static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    static extern int CoRegisterClassObject(in Guid id, [MarshalAs(UnmanagedType.IUnknown)] object factory,
        uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll")]
    static extern int CoRevokeClassObject(uint cookie);
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr outer, in Guid iid, out IntPtr result);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}

[ComVisible(true)]
sealed class WidgetFactory : IClassFactory
{
    readonly WidgetProvider provider = new();

    public int CreateInstance(IntPtr outer, in Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
        IntPtr pointer = IntPtr.Zero;
        try
        {
            pointer = MarshalInspectable<IWidgetProvider>.FromManaged(provider);
            return Marshal.QueryInterface(pointer, in iid, out result);
        }
        catch (Exception error) { return Marshal.GetHRForException(error); }
        finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
    }

    public int LockServer(bool locked) => 0;
}
