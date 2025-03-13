using System.Text.Json.Serialization;

namespace Medoz.X;

public record ErrorResponse(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("status")] string? Status
);