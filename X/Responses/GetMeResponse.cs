using System.Text.Json.Serialization;

public record GetMeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("email_verified")] bool EmailVerified,
    [property: JsonPropertyName("picture")] string Picture,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt
);