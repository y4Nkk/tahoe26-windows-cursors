using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TahoeCursorStudio.Models;
using TahoeCursorStudio.Services;

namespace TahoeCursorStudio.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly BuiltInThemeProvider _builtInThemeProvider;
    private readonly ThemeManifestService _manifestService;
    private readonly CursorPreviewService _previewService;
    private readonly CursorInstaller _installer;
    private readonly Func<string, string?> _folderPicker;
    private readonly Action<string, string> _showInformation;
    private readonly Action<string, string> _showError;

    private string _themeRoot = string.Empty;
    private string _variantId = "dark";
    private ThemePackage? _package;
    private bool _isBusy;
    private string _themeTitle = "光标主题工作室";
    private string _summary = string.Empty;
    private string _footer = "正在载入主题……";
    private string _systemStatus = string.Empty;

    public MainWindowViewModel(
        BuiltInThemeProvider builtInThemeProvider,
        ThemeManifestService manifestService,
        CursorPreviewService previewService,
        CursorInstaller installer,
        Func<string, string?> folderPicker,
        Action<string, string> showInformation,
        Action<string, string> showError)
    {
        _builtInThemeProvider = builtInThemeProvider;
        _manifestService = manifestService;
        _previewService = previewService;
        _installer = installer;
        _folderPicker = folderPicker;
        _showInformation = showInformation;
        _showError = showError;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy);
        ChooseThemeCommand = new RelayCommand(_ => ChooseTheme(), _ => !IsBusy);
        SelectVariantCommand = new RelayCommand(SelectVariant, _ => !IsBusy);
    }

    public ObservableCollection<CursorRoleViewModel> Roles { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public RelayCommand ChooseThemeCommand { get; }
    public RelayCommand SelectVariantCommand { get; }

    public string ThemeRoot { get => _themeRoot; private set => Set(ref _themeRoot, value); }
    public string ThemeTitle { get => _themeTitle; private set => Set(ref _themeTitle, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Footer { get => _footer; private set => Set(ref _footer, value); }
    public string SystemStatus { get => _systemStatus; private set => Set(ref _systemStatus, value); }
    public bool IsDarkSelected => _variantId == "dark";
    public bool IsLightSelected => _variantId == "light";
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanInteract));
            RefreshCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            ChooseThemeCommand.RaiseCanExecuteChanged();
            SelectVariantCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanInteract => !IsBusy;

    public async Task InitializeAsync()
    {
        ThemeRoot = await Task.Run(_builtInThemeProvider.Materialize);
        SystemStatus = "内置资源已就绪 · 应用时会自动处理当前 Windows 会话";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(ThemeRoot)) return;
        IsBusy = true;
        Footer = "正在重新读取当前系统光标和 256×256 主题资源……";
        try
        {
            await RefreshCoreAsync();
        }
        catch (Exception exception)
        {
            Footer = $"预览失败：{exception.Message}";
            _showError(exception.Message, "光标主题工作室");
        }
        finally { IsBusy = false; }
    }

    private CursorRoleViewModel CreateRole(ThemePackage package, CursorRole role)
    {
        var preview = _previewService.Build(package, role);
        return new CursorRoleViewModel(
            role.Label,
            role.SystemId is null ? "仅注册表角色" : "系统标准角色",
            role.File,
            role.SystemId,
            preview.Current,
            preview.Target,
            preview.Diff,
            preview.Percent);
    }

    private async Task ApplyAsync()
    {
        if (_package is null) return;
        IsBusy = true;
        Footer = "正在安装持久光标方案并修复当前 Windows 会话……";
        try
        {
            var result = await Task.Run(() => _installer.Apply(_package));
            if (!result.Success) throw new InvalidOperationException(string.Join("；", result.SessionFailures));
            await RefreshCoreAsync();
            var sessionText = result.ReloadSucceeded
                ? "Windows 标准光标加载器已完成切换。"
                : $"Windows 重新加载被拒绝，已直接替换当前会话的 {result.SessionApplied} 个系统角色。";
            _showInformation($"主题：{result.ThemeName}\n已安装角色：{result.RegistryRoles}\n{sessionText}", "光标主题已应用");
            Footer = $"已应用 {result.ThemeName}。";
        }
        catch (Exception exception)
        {
            Footer = $"应用失败：{exception.Message}";
            _showError(exception.Message, "光标主题工作室");
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshCoreAsync()
    {
        var package = await Task.Run(() => _manifestService.Load(ThemeRoot, _variantId));
        var rows = await Task.Run(() => package.Manifest.Roles
            .Select(role => CreateRole(package, role))
            .ToArray());
        _package = package;
        Roles.Clear();
        foreach (var row in rows) Roles.Add(row);
        var different = rows.Count(row => row.Percent > 1);
        ThemeTitle = $"{package.Manifest.Name} · {package.Variant.Label} 光标主题工作室";
        Summary = $"{rows.Length} 个角色  /  {different} 个差异  /  {rows.Length - different} 个一致  /  256×256 标准上限";
        Footer = "就绪。当前 Windows 配置与目标资源统一到 CUR 标准上限 256×256。";
    }

    private void ChooseTheme()
    {
        var selected = _folderPicker(ThemeRoot);
        if (string.IsNullOrWhiteSpace(selected)) return;
        try
        {
            var package = _manifestService.Load(selected, null);
            ThemeRoot = package.Root;
            _variantId = package.Manifest.DefaultVariant;
            OnPropertyChanged(nameof(IsDarkSelected));
            OnPropertyChanged(nameof(IsLightSelected));
            _ = RefreshAsync();
        }
        catch (Exception exception) { _showError(exception.Message, "光标主题工作室"); }
    }

    private void SelectVariant(object? parameter)
    {
        var requested = parameter as string;
        if (requested is not ("dark" or "light") || requested == _variantId) return;
        _variantId = requested;
        OnPropertyChanged(nameof(IsDarkSelected));
        OnPropertyChanged(nameof(IsLightSelected));
        _ = RefreshAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed record CursorRoleViewModel(
    string Label,
    string RoleType,
    string FileName,
    uint? SystemId,
    ImageSource Current,
    ImageSource Target,
    ImageSource Diff,
    int Percent)
{
    public string ResultText => Percent <= 1 ? "完全一致" : $"差异 {Percent}%";
    public string ResourceText => SystemId is null
        ? $"资源：{FileName}  |  仅注册表角色"
        : $"资源：{FileName}  |  系统标识：{SystemId}";
    public bool IsMatching => Percent <= 1;
}
