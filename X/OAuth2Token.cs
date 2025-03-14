using System.Text.Json.Serialization;

namespace Medoz.X;

using System;
using System.Collections.Generic;

public record OAuth2Token(
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("scope"), JsonConverter(typeof(ScopesJsonConverter))] Scopes Scope,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken)
{
    public virtual bool Equals(OAuth2Token? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return TokenType == other.TokenType &&
               ExpiresIn == other.ExpiresIn &&
               AccessToken == other.AccessToken &&
               EqualityComparer<Scopes>.Default.Equals(Scope, other.Scope) &&
               RefreshToken == other.RefreshToken;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TokenType, ExpiresIn, AccessToken, Scope, RefreshToken);
    }
}