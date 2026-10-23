using System.Runtime.CompilerServices;
using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

internal static class TestSetup
{
    // The unit tests never need real USB access; loading libusb here crashed the test host from a finalizer.
    [ModuleInitializer]
    internal static void DisableUsb() => UsbDeviceService.IsEnabled = false;
}
