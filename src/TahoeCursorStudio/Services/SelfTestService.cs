using System.Security.Cryptography;
using System.Text.Json;

namespace TahoeCursorStudio.Services;

public sealed class SelfTestService
{
    public SelfTestReport Run(string reportPath)
    {
        var provider = new BuiltInThemeProvider();
        var root = provider.Materialize();
        var manifestService = new ThemeManifestService();
        var previewService = new CursorPreviewService();
        var dark = manifestService.Load(root, "dark");
        var light = manifestService.Load(root, "light");
        var expectedSizes = new[] { 32, 40, 48, 56, 64, 80, 96, 112, 128, 256 };
        var pairsDifferent = 0;
        var maximumLoadable = 0;
        var validCurEntries = 0;

        foreach (var role in dark.Manifest.Roles)
        {
            var darkPath = Path.Combine(dark.SourcePath, role.File);
            var lightPath = Path.Combine(light.SourcePath, role.File);
            if (!SHA256.HashData(File.ReadAllBytes(darkPath)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(lightPath))))
                pairsDifferent++;
            if (previewService.CanLoadAtMaximumSize(darkPath) && previewService.CanLoadAtMaximumSize(lightPath))
                maximumLoadable += 2;
            if (Path.GetExtension(role.File).Equals(".cur", StringComparison.OrdinalIgnoreCase))
            {
                if (CursorFormatInspector.ReadCurSizes(darkPath).SequenceEqual(expectedSizes)
                    && CursorFormatInspector.ReadCurSizes(lightPath).SequenceEqual(expectedSizes))
                    validCurEntries += 2;
            }
        }

        var report = new SelfTestReport(
            SchemaVersion: dark.Manifest.SchemaVersion,
            Variants: dark.Manifest.Variants.Count,
            RolesPerVariant: dark.Manifest.Roles.Count,
            ResourceFiles: Directory.GetFiles(Path.Combine(root, "Cursors"), "*.*", SearchOption.AllDirectories).Length,
            VariantPairsDifferent: pairsDifferent,
            MaximumSizeLoadable: maximumLoadable,
            StaticCurFilesWithExactSizes: validCurEntries,
            PreviewRaster: CursorPreviewService.RasterSize,
            Success: dark.Manifest.SchemaVersion == 2
                     && dark.Manifest.Variants.Count == 2
                     && dark.Manifest.Roles.Count == 17
                     && pairsDifferent == 17
                     && maximumLoadable == 34
                     && validCurEntries == 30);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report;
    }
}

public sealed record SelfTestReport(
    int SchemaVersion,
    int Variants,
    int RolesPerVariant,
    int ResourceFiles,
    int VariantPairsDifferent,
    int MaximumSizeLoadable,
    int StaticCurFilesWithExactSizes,
    int PreviewRaster,
    bool Success);
