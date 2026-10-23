using System.Text.Json.Serialization;

namespace Rama.Core.Accounts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AccountsData))]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Código source-gen")]
internal sealed partial class AccountsJsonContext : JsonSerializerContext
{
}
