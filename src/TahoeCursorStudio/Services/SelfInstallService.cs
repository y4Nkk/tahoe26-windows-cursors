using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TahoeCursorStudio.Infrastructure;

namespace TahoeCursorStudio.Services;

public sealed class SelfInstallService
{
    public const string ProductName = "Tahoe 26 光标主题工作室";
    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "TahoeCursorStudio");
    public static string InstalledExecutable => Path.Combine(InstallDirectory, "TahoeCursorStudio.exe");

    public string Install()
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前应用程序路径。");
        Directory.CreateDirectory(InstallDirectory);
        if (!Path.GetFullPath(currentExecutable).Equals(Path.GetFullPath(InstalledExecutable), StringComparison.OrdinalIgnoreCase))
            File.Copy(currentExecutable, InstalledExecutable, true);

        CreateShortcuts(InstalledExecutable);
        using var uninstall = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TahoeCursorStudio", true)
            ?? throw new InvalidOperationException("无法注册卸载信息。");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        uninstall.SetValue("DisplayName", ProductName, RegistryValueKind.String);
        uninstall.SetValue("DisplayVersion", version, RegistryValueKind.String);
        uninstall.SetValue("Publisher", "Tahoe Cursor Studio", RegistryValueKind.String);
        uninstall.SetValue("InstallLocation", InstallDirectory, RegistryValueKind.String);
        uninstall.SetValue("DisplayIcon", InstalledExecutable, RegistryValueKind.String);
        uninstall.SetValue("UninstallString", $"\"{InstalledExecutable}\" --uninstall", RegistryValueKind.String);
        uninstall.SetValue("QuietUninstallString", $"\"{InstalledExecutable}\" --uninstall --quiet", RegistryValueKind.String);
        uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        uninstall.SetValue("EstimatedSize", (int)Math.Ceiling(new FileInfo(InstalledExecutable).Length / 1024d), RegistryValueKind.DWord);
        return InstalledExecutable;
    }

    public void BeginUninstall()
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前应用程序路径。");
        var helper = Path.Combine(Path.GetTempPath(), $"TahoeCursorStudio-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(currentExecutable, helper, true);
        Process.Start(new ProcessStartInfo
        {
            FileName = helper,
            Arguments = $"--uninstall-final \"{InstallDirectory}\" {Environment.ProcessId}",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    public void FinishUninstall(string installDirectory, int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            parent.WaitForExit(15000);
        }
        catch { }

        new CursorInstaller().RestorePreviousScheme();
        DeleteShortcuts();
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\TahoeCursorStudio", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\TahoeCursorStudio", false);

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TahoeCursorStudio");
        if (Directory.Exists(appData)) Directory.Delete(appData, true);
        if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, true);

        var helper = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(helper))
            NativeMethods.MoveFileExW(helper, null, 0x00000004);
    }

    private static void CreateShortcuts(string target)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        CreateShortcut(Path.Combine(desktop, $"{ProductName}.lnk"), target);
        CreateShortcut(Path.Combine(programs, $"{ProductName}.lnk"), target);
    }

    private static void DeleteShortcuts()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.lnk"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{ProductName}.lnk")
                 })
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CreateShortcut(string shortcutPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host 不可用。");
        object? shell = null;
        object? shortcutObject = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic dynamicShell = shell ?? throw new InvalidOperationException("无法创建快捷方式服务。");
            dynamic shortcut = dynamicShell.CreateShortcut(shortcutPath);
            shortcutObject = shortcut;
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = $"{target},0";
            shortcut.Description = "深浅双色、256×256 预览与一键应用";
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        finally
        {
            if (shortcutObject is not null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
