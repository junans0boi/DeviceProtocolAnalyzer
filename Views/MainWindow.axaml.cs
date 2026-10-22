using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using DeviceProtocolAnalyzer.ViewModels;

namespace DeviceProtocolAnalyzer.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private readonly DispatcherTimer _portTimer;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;

        _portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _portTimer.Tick += (_, _) => _viewModel?.PollPorts();
        _portTimer.Start();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LogEntries.CollectionChanged -= OnLogEntriesChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.AutoScroll != true || _viewModel.LogEntries.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(_viewModel.LogEntries[^1]));
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _portTimer.Stop();
        _viewModel?.Dispose();
    }
}
