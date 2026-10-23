using Avalonia;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DeviceProtocolAnalyzer;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLibUsbResolver();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Homebrew installs libusb outside the default dyld search path on macOS.
    private static void ConfigureLibUsbResolver()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(LibUsbDotNet.LibUsb.UsbContext).Assembly, ResolveLibUsb);
    }

    private static IntPtr ResolveLibUsb(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "libusb-1.0")
        {
            return IntPtr.Zero;
        }

        foreach (var directory in new[] { "/opt/homebrew/lib", "/usr/local/lib" })
        {
            var path = Path.Combine(directory, "libusb-1.0.dylib");
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
