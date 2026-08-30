using System.Text.Json.Serialization;

namespace TahoeCursorStudio.Models;

public sealed class ThemeManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("sourceDirectory")]
    public string SourceDirectory { get; init; } = string.Empty;

    [JsonPropertyName("defaultVariant")]
    public string DefaultVariant { get; init; } = string.Empty;

    [JsonPropertyName("variants")]
    public IReadOnlyList<ThemeVariant> Variants { get; init; } = [];

    [JsonPropertyName("roles")]
    public IReadOnlyList<CursorRole> Roles { get; init; } = [];
}

public sealed class ThemeVariant
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("schemeName")]
    public string SchemeName { get; init; } = string.Empty;

    [JsonPropertyName("directory")]
    public string Directory { get; init; } = string.Empty;
}

public sealed class CursorRole
{
    [JsonPropertyName("registry")]
    public string Registry { get; init; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("systemId")]
    public uint? SystemId { get; init; }
}

public sealed record ThemePackage(string Root, ThemeManifest Manifest, ThemeVariant Variant)
{
    public string SourcePath => Path.Combine(Root, Manifest.SourceDirectory, Variant.Directory);
}

public sealed record ApplyResult(
    string ThemeName,
    string VariantId,
    int RegistryRoles,
    bool ReloadSucceeded,
    int ReloadError,
    int SessionApplied,
    int SessionSkipped,
    IReadOnlyList<string> SessionFailures)
{
    public bool SessionRepairUsed => !ReloadSucceeded;
    public bool Success => SessionFailures.Count == 0;
}
