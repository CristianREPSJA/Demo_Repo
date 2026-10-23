using System.Text.Json.Serialization;

namespace Rama.Core.Bitbucket;

// DTOs de la API 2.0 de Bitbucket Cloud (snake_case).
internal sealed record BbPage<T>(List<T>? Values, string? Next);

internal sealed record BbWorkspaceDto(string? Slug, string? Name);

internal sealed record BbUserDto(string? DisplayName, string? Nickname, string? AccountId);

internal sealed record BbNamed(string? Name);

internal sealed record BbLink(string? Name, string? Href);

internal sealed record BbRepoLinks([property: JsonPropertyName("clone")] List<BbLink>? CloneLinks);

internal sealed record BbRepoDto(
    string? Name, string? Slug, string? FullName, string? Description, BbNamed? Project,
    BbNamed? Mainbranch, DateTimeOffset? UpdatedOn, long? Size, bool? IsPrivate, BbRepoLinks? Links);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BbUserDto))]
[JsonSerializable(typeof(BbPage<BbWorkspaceDto>))]
[JsonSerializable(typeof(BbPage<BbRepoDto>))]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Código source-gen")]
internal sealed partial class BitbucketJsonContext : JsonSerializerContext
{
}
