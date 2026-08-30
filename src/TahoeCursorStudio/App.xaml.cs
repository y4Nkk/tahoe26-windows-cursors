using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using TahoeCursorStudio.Services;

namespace TahoeCursorStudio;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");

        if (e.Args.Length == 0)
        {
            _singleInstanceMutex = new Mutex(true, @"Local\TahoeCursorStudio", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("光标主题工作室已经在运行。", "Tahoe 26 光标主题工作室",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            new MainWindow().Show();
            return;
        }

        var exitCode = await RunCommandAsync(e.Args);
        Shutdown(exitCode);
    }

    private static async Task<int> RunCommandAsync(string[] args)
    {
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--self-test":
                {
                    var reportPath = RequireArgument(args, 1, "--self-test 需要报告文件路径。");
                    var report = await Task.Run(() => new SelfTestService().Run(reportPath));
                    return report.Success ? 0 : 2;
                }
                case "--apply":
                {
                    var variant = RequireArgument(args, 1, "--apply 需要 dark 或 light 参数。").ToLowerInvariant();
                    if (variant is not ("dark" or "light"))
                        throw new ArgumentException("外观参数只能是 dark 或 light。");
                    var root = await Task.Run(() => new BuiltInThemeProvider().Materialize());
                    var package = new ThemeManifestService().Load(root, variant);
                    var result = await Task.Run(() => new CursorInstaller().Apply(package));
                    if (args.Length >= 3)
                    {
                        var reportPath = Path.GetFullPath(args[2]);
                        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                        await File.WriteAllTextAsync(reportPath,
                            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    return result.Success ? 0 : 3;
                }
                case "--install":
                {
                    var installed = await Task.Run(() => new SelfInstallService().Install());
                    if (!args.Contains("--quiet", StringComparer.OrdinalIgnoreCase))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = installed,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(installed)!
                        });
                    }
                    return 0;
                }
                case "--uninstall":
                    new SelfInstallService().BeginUninstall();
                    return 0;
                case "--uninstall-final":
                {
                    var installDirectory = Path.GetFullPath(RequireArgument(args, 1, "缺少安装目录。"));
                    if (!int.TryParse(RequireArgument(args, 2, "缺少父进程标识。"), out var parentProcessId))
                        throw new ArgumentException("父进程标识无效。");
                    await Task.Run(() => new SelfInstallService().FinishUninstall(installDirectory, parentProcessId));
                    return 0;
                }
                default:
                    throw new ArgumentException($"未知命令：{args[0]}");
            }
        }
        catch (Exception exception)
        {
            var errorPath = Path.Combine(Path.GetTempPath(), "TahoeCursorStudio-last-error.txt");
            await File.WriteAllTextAsync(errorPath, exception.ToString());
            if (!args.Contains("--quiet", StringComparer.OrdinalIgnoreCase))
                MessageBox.Show($"操作失败：{exception.Message}\n\n错误记录：{errorPath}",
                    "Tahoe 26 光标主题工作室", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static string RequireArgument(IReadOnlyList<string> args, int index, string message) =>
        index < args.Count && !string.IsNullOrWhiteSpace(args[index])
            ? args[index]
            : throw new ArgumentException(message);
}
