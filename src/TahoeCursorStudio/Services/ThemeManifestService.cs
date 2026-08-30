using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TahoeCursorStudio.Models;

namespace TahoeCursorStudio.Services;

public sealed class ThemeManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public ThemePackage Load(string root, string? variantId = null)
    {
        var resolvedRoot = Path.GetFullPath(root);
        var manifestPath = Path.Combine(resolvedRoot, "cursor-theme.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"找不到光标主题清单：{manifestPath}");

        var json = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("光标主题清单为空或格式无效。");
        Validate(manifest);

        var selectedId = string.IsNullOrWhiteSpace(variantId) ? manifest.DefaultVariant : variantId;
        var variant = manifest.Variants.SingleOrDefault(item => item.Id == selectedId)
            ?? throw new InvalidDataException($"找不到光标外观：{selectedId}");
        var package = new ThemePackage(resolvedRoot, manifest, variant);
        if (!Directory.Exists(package.SourcePath))
            throw new DirectoryNotFoundException($"找不到光标资源目录：{package.SourcePath}");
        foreach (var role in manifest.Roles)
        {
            var resource = Path.Combine(package.SourcePath, role.File);
            if (!File.Exists(resource)) throw new FileNotFoundException($"缺少光标资源：{resource}", resource);
        }
        return package;
    }

    public void Validate(ThemeManifest manifest)
    {
        if (manifest.SchemaVersion != 2)
            throw new InvalidDataException($"不支持此光标主题清单版本：{manifest.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("光标主题标识和名称不能为空。");
        if (string.IsNullOrWhiteSpace(manifest.SourceDirectory))
            throw new InvalidDataException("清单中的 sourceDirectory 字段不能为空。");
        if (manifest.Roles.Count == 0)
            throw new InvalidDataException("光标主题必须至少定义一个角色。");
        var ids = manifest.Variants.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!ids.SequenceEqual(new[] { "dark", "light" }, StringComparer.Ordinal))
            throw new InvalidDataException("光标主题必须且只能定义 dark 和 light 两种外观。");
        if (manifest.DefaultVariant is not ("dark" or "light"))
            throw new InvalidDataException("defaultVariant 必须是 dark 或 light。");
        if (manifest.Roles.Select(item => item.Registry).Distinct(StringComparer.Ordinal).Count() != manifest.Roles.Count)
            throw new InvalidDataException("光标主题包含重复的注册表角色。");
    }
}

public sealed class BuiltInThemeProvider
{
    private readonly Assembly _assembly = typeof(BuiltInThemeProvider).Assembly;

    public string ThemeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TahoeCursorStudio", "Themes", "Tahoe26");

    public string Materialize()
    {
        Directory.CreateDirectory(ThemeRoot);
        WriteResource("TahoeCursorStudio.Assets.cursor-theme.json", Path.Combine(ThemeRoot, "cursor-theme.json"));
        foreach (var variant in new[] { "dark", "light" })
        {
            var output = Path.Combine(ThemeRoot, "Cursors", variant);
            Directory.CreateDirectory(output);
            foreach (var resourceName in _assembly.GetManifestResourceNames()
                         .Where(name => name.Contains($".Assets.Cursors.{variant}.", StringComparison.Ordinal)))
            {
                var extensionIndex = resourceName.LastIndexOf($".{variant}.", StringComparison.Ordinal);
                var fileName = resourceName[(extensionIndex + variant.Length + 2)..];
                WriteResource(resourceName, Path.Combine(output, fileName));
            }
        }
        return ThemeRoot;
    }

    private void WriteResource(string resourceName, string destination)
    {
        using var input = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到嵌入资源：{resourceName}");
        if (File.Exists(destination))
        {
            using var existing = File.OpenRead(destination);
            if (existing.Length == input.Length && StreamsEqual(existing, input)) return;
            input.Position = 0;
        }
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    private static bool StreamsEqual(Stream first, Stream second)
    {
        Span<byte> a = stackalloc byte[8192];
        Span<byte> b = stackalloc byte[8192];
        while (true)
        {
            var readA = first.Read(a);
            var readB = second.Read(b);
            if (readA != readB) return false;
            if (readA == 0) return true;
            if (!a[..readA].SequenceEqual(b[..readB])) return false;
        }
    }
}
