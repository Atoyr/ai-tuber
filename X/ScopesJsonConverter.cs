using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Medoz.X;

public class ScopesJsonConverter : JsonConverter<Scopes>
{
    public override Scopes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string scopeString = reader.GetString() ?? "none";
            return ScopesExtensions.FromScopeString(scopeString);
        }

        throw new JsonException($"予期しないトークンタイプ: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, Scopes value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToScopeString());
    }
}