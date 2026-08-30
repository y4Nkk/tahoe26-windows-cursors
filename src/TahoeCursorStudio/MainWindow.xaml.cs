using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using TahoeCursorStudio.Infrastructure;
using TahoeCursorStudio.Services;
using TahoeCursorStudio.ViewModels;

namespace TahoeCursorStudio;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(
            new BuiltInThemeProvider(),
            new ThemeManifestService(),
            new CursorPreviewService(),
            new CursorInstaller(),
            PickFolder,
            (message, title) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information),
            (message, title) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error));
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) => Application.Current.Shutdown();
    }

    private string? PickFolder(string initialPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "请选择包含 cursor-theme.json 的主题文件夹",
            InitialDirectory = Directory.Exists(initialPath) ? initialPath : null,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        var rounded = 2;
        NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }

}
