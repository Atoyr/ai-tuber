using System.Text.Json.Serialization;

namespace Medoz.X;

public record OAuth2Token(
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("scope"), JsonConverter(typeof(ScopesJsonConverter))] Scopes Scope,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);