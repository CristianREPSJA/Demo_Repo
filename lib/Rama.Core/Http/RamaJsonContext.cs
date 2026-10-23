using System.Text.Json.Serialization;

namespace Rama.Core.Http;

// Contexto source-gen; agregar aquí los DTOs de cada integración.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Código source-gen")]
public partial class RamaJsonContext : JsonSerializerContext
{
}
