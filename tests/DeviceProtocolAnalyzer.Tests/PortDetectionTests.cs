using DeviceProtocolAnalyzer.ViewModels;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class PortDetectionTests
{
    [Fact]
    public void NewPortIsSelectedAndAnnouncedThenRemovalIsAnnounced()
    {
        using var viewModel = new MainViewModel();
        viewModel.ApplyPorts(["COM3"]);

        viewModel.ApplyPorts(["COM3", "COM7"]);
        Assert.Equal("COM7", viewModel.SelectedPort);
        Assert.Contains("COM7", viewModel.PortNotice);

        viewModel.ApplyPorts(["COM3"]);
        Assert.Equal("COM3", viewModel.SelectedPort);
        Assert.Contains("제거", viewModel.PortNotice);
    }

    [Fact]
    public void UnchangedPortsKeepNoticeAndSelection()
    {
        using var viewModel = new MainViewModel();
        viewModel.ApplyPorts(["COM3", "COM7"]);
        var notice = viewModel.PortNotice;

        viewModel.ApplyPorts(["COM3", "COM7"]);

        Assert.Equal(notice, viewModel.PortNotice);
        Assert.Equal("COM7", viewModel.SelectedPort);
    }
}
