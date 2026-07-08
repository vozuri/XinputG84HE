using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XinputG84HE.ViewModels;

namespace XinputG84HE;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;
    private bool _reallyClose;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_reallyClose)
        {
            _viewModel.Shutdown();
            return;
        }

        // Minimize to tray instead of exiting - emulation should keep running
        // in the background even if the window is "closed".
        e.Cancel = true;
        Hide();
    }

    private void OnPresetTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetTileViewModel tile })
        {
            tile.SelectCommand.Execute(null);
        }
    }

    private void OnPresetTileRightClick(object sender, MouseButtonEventArgs e)
    {
        // Reserved for a future right-click context menu.
    }

    private void OnPresetRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetTileViewModel tile })
        {
            if (e.Key == Key.Enter)
            {
                tile.CommitRenameCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                tile.CancelRenameCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnPresetRenameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetTileViewModel tile } && tile.IsEditingName)
        {
            tile.CommitRenameCommand.Execute(null);
        }
    }

    private void OnCanvasRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsBinding)
        {
            _viewModel.CancelBindCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e) => ShowWindow();

    private void OnTrayShow(object sender, RoutedEventArgs e) => ShowWindow();

    private void OnTrayToggleEmulation(object sender, RoutedEventArgs e) =>
        _viewModel.ToggleEmulationCommand.Execute(null);

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        _reallyClose = true;
        TrayIcon.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => OnTrayExit(sender, e);

    /// <summary>
    /// Redundant safety net alongside the IsChecked binding: explicitly pushes
    /// the toggle's new state into the view model the moment it's clicked,
    /// in case of any binding-timing edge case.
    /// </summary>
    private void OnBlockMappedKeysClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            _viewModel.BlockMappedKeys = toggle.IsChecked ?? false;
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
