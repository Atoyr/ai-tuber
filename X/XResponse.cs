using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Medoz.X;

/// <summary>
/// Represents a response from the X API
/// </summary>
public class XResponse
{
    /// <summary>
    /// Indicates whether the request was successful
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// The HTTP status code of the response
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// The content of the response as a string
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Gets the deserialized content of the response
    /// </summary>
    /// <typeparam name="T">The type to deserialize to</typeparam>
    /// <returns>The deserialized content</returns>
    public T? GetContent<T>()
    {
        if (string.IsNullOrEmpty(Content))
            return default;

        return JsonSerializer.Deserialize<T>(Content);
    }
}
