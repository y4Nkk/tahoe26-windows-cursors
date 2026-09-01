using System.Runtime.InteropServices;
using Microsoft.Win32;
using TahoeCursorStudio.Infrastructure;
using TahoeCursorStudio.Models;

namespace TahoeCursorStudio.Services;

public sealed class CursorInstaller
{
    private static readonly IReadOnlyDictionary<string, (string File, uint? SystemId)> DefaultRoles =
        new Dictionary<string, (string, uint?)>(StringComparer.Ordinal)
        {
            ["Arrow"] = ("aero_arrow.cur", 32512), ["Help"] = ("aero_helpsel.cur", 32651),
            ["AppStarting"] = ("aero_working.ani", 32650), ["Wait"] = ("aero_busy.ani", 32514),
            ["Crosshair"] = ("cross_r.cur", 32515), ["IBeam"] = ("beam_r.cur", 32513),
            ["NWPen"] = ("aero_pen.cur", null), ["No"] = ("aero_unavail.cur", 32648),
            ["SizeNS"] = ("aero_ns.cur", 32645), ["SizeWE"] = ("aero_ew.cur", 32644),
            ["SizeNWSE"] = ("aero_nwse.cur", 32642), ["SizeNESW"] = ("aero_nesw.cur", 32643),
            ["SizeAll"] = ("aero_move.cur", 32646), ["UpArrow"] = ("aero_up.cur", 32516),
            ["Hand"] = ("aero_link.cur", 32649), ["Pin"] = ("aero_pin.cur", 32671),
            ["Person"] = ("aero_person.cur", 32672)
        };

    public ApplyResult Apply(ThemePackage package)
    {
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TahoeCursorStudio", "Schemes");
        var destination = Path.Combine(installRoot, package.Variant.Id);
        Directory.CreateDirectory(destination);

        foreach (var role in package.Manifest.Roles)
        {
            File.Copy(Path.Combine(package.SourcePath, role.File), Path.Combine(destination, role.File), true);
        }

        using var cursorKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors", true)
            ?? throw new InvalidOperationException("无法打开当前用户的光标注册表设置。");
        EnsureBackup(cursorKey, package.Manifest.Roles);
        foreach (var role in package.Manifest.Roles)
            cursorKey.SetValue(role.Registry, Path.Combine(destination, role.File), RegistryValueKind.String);
        cursorKey.SetValue(string.Empty, package.Variant.SchemeName, RegistryValueKind.String);
        cursorKey.SetValue("Scheme Source", 1, RegistryValueKind.DWord);

        using var schemesKey = cursorKey.CreateSubKey("Schemes", true)
            ?? throw new InvalidOperationException("无法打开光标方案注册表设置。");
        var schemeValue = string.Join(',', package.Manifest.Roles.Select(role => Path.Combine(destination, role.File)));
        schemesKey.SetValue(package.Variant.SchemeName, schemeValue, RegistryValueKind.String);

        var reloaded = NativeMethods.SystemParametersInfoW(
            NativeMethods.SpiSetCursors, 0, 0,
            NativeMethods.SpifUpdateIniFile | NativeMethods.SpifSendChange);
        var reloadError = reloaded ? 0 : Marshal.GetLastWin32Error();
        var applied = 0;
        var skipped = 0;
        var failures = new List<string>();

        if (!reloaded)
        {
            foreach (var role in package.Manifest.Roles)
            {
                if (role.SystemId is null)
                {
                    skipped++;
                    continue;
                }
                var path = Path.Combine(destination, role.File);
                var cursor = NativeMethods.LoadImageW(0, path, NativeMethods.ImageCursor, 0, 0, NativeMethods.LrLoadFromFile | NativeMethods.LrDefaultSize);
                if (cursor == 0)
                {
                    failures.Add($"{role.Registry}（加载失败：{Marshal.GetLastWin32Error()}）");
                    continue;
                }
                if (NativeMethods.SetSystemCursor(cursor, role.SystemId.Value))
                {
                    applied++;
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    NativeMethods.DestroyCursor(cursor);
                    failures.Add($"{role.Registry}（系统错误：{error}）");
                }
            }
        }

        return new ApplyResult(
            package.Variant.SchemeName,
            package.Variant.Id,
            package.Manifest.Roles.Count,
            reloaded,
            reloadError,
            applied,
            skipped,
            failures);
    }

    public void RestorePreviousScheme()
    {
        using var cursorKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors", true)
            ?? throw new InvalidOperationException("无法打开当前用户的光标注册表设置。");
        using var appKey = Registry.CurrentUser.OpenSubKey(@"Software\TahoeCursorStudio", true);
        using var backup = appKey?.OpenSubKey("CursorBackup");

        foreach (var role in DefaultRoles)
        {
            var value = backup?.GetValue(role.Key) as string;
            if (string.IsNullOrWhiteSpace(value)) value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors", role.Value.File);
            cursorKey.SetValue(role.Key, value, RegistryValueKind.String);
        }
        var schemeName = backup?.GetValue("Default") as string;
        cursorKey.SetValue(string.Empty, string.IsNullOrWhiteSpace(schemeName) ? "Windows Default" : schemeName, RegistryValueKind.String);
        cursorKey.SetValue("Scheme Source", Convert.ToInt32(backup?.GetValue("Scheme Source") ?? 1), RegistryValueKind.DWord);
        using (var schemes = cursorKey.OpenSubKey("Schemes", true))
        {
            schemes?.DeleteValue("Tahoe 26 深色", false);
            schemes?.DeleteValue("Tahoe 26 浅色", false);
        }

        var reloaded = NativeMethods.SystemParametersInfoW(
            NativeMethods.SpiSetCursors, 0, 0,
            NativeMethods.SpifUpdateIniFile | NativeMethods.SpifSendChange);
        if (!reloaded)
        {
            foreach (var role in DefaultRoles.Where(item => item.Value.SystemId is not null))
            {
                var path = cursorKey.GetValue(role.Key) as string;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                var handle = NativeMethods.LoadImageW(0, path, NativeMethods.ImageCursor, 0, 0, NativeMethods.LrLoadFromFile | NativeMethods.LrDefaultSize);
                if (handle == 0) continue;
                if (!NativeMethods.SetSystemCursor(handle, role.Value.SystemId!.Value)) NativeMethods.DestroyCursor(handle);
            }
        }
    }

    private static void EnsureBackup(RegistryKey cursorKey, IReadOnlyList<CursorRole> roles)
    {
        using var appKey = Registry.CurrentUser.CreateSubKey(@"Software\TahoeCursorStudio", true)
            ?? throw new InvalidOperationException("无法创建应用设置。");
        if (Convert.ToInt32(appKey.GetValue("BackupCreated") ?? 0) == 1) return;
        using var backup = appKey.CreateSubKey("CursorBackup", true)
            ?? throw new InvalidOperationException("无法创建光标备份。");
        var currentScheme = cursorKey.GetValue(string.Empty) as string ?? string.Empty;
        var appManagedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TahoeCursorStudio");
        var existingIsTahoe = currentScheme.StartsWith("Tahoe 26", StringComparison.OrdinalIgnoreCase)
            || roles.Any(role => (cursorKey.GetValue(role.Registry) as string)
                ?.Contains(appManagedRoot, StringComparison.OrdinalIgnoreCase) == true);
        foreach (var role in roles)
        {
            var existing = existingIsTahoe ? null : cursorKey.GetValue(role.Registry) as string;
            if (string.IsNullOrWhiteSpace(existing) && DefaultRoles.TryGetValue(role.Registry, out var fallback))
                existing = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors", fallback.File);
            backup.SetValue(role.Registry, existing ?? string.Empty, RegistryValueKind.String);
        }
        backup.SetValue("Default", existingIsTahoe ? "Windows Default" : currentScheme, RegistryValueKind.String);
        backup.SetValue("Scheme Source", Convert.ToInt32(cursorKey.GetValue("Scheme Source") ?? 1), RegistryValueKind.DWord);
        appKey.SetValue("BackupCreated", 1, RegistryValueKind.DWord);
    }
}
