using DeviceProtocolAnalyzer.Models;
using DeviceProtocolAnalyzer.ViewModels;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class UsbDeviceSelectionTests
{
    private static readonly UsbDeviceOption Eagle = new(0x2160, 0xB15A, 1, 5);
    private static readonly UsbDeviceOption Other = new(0x1234, 0x0001, 1, 6);

    [Fact]
    public void NewUsbDeviceIsSelectedAndAnnouncedThenRemovalIsAnnounced()
    {
        using var viewModel = new MainViewModel();
        viewModel.ApplyUsbDevices([Other]);

        viewModel.ApplyUsbDevices([Other, Eagle]);
        Assert.Equal(Eagle, viewModel.SelectedUsbDevice);
        Assert.Contains("2160:B15A", viewModel.PortNotice);

        viewModel.ApplyUsbDevices([Other]);
        Assert.Equal(Other, viewModel.SelectedUsbDevice);
        Assert.Contains("제거", viewModel.PortNotice);
    }

    [Fact]
    public void UsbModeCannotSendAndDisplaysVidPid()
    {
        using var viewModel = new MainViewModel();
        viewModel.SelectedTransport = "USB";

        Assert.True(viewModel.IsUsbMode);
        Assert.False(viewModel.IsSerialMode);
        Assert.False(viewModel.SendCommand.CanExecute(null));
        Assert.Equal("USB 2160:B15A (버스 1, 주소 5)", Eagle.Display);
    }
}
