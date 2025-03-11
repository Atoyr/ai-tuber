using System;
using System.Security.Cryptography;
using System.Text;

namespace Medoz.X;

public class Session
{
    private readonly int _codeVerifierLength = 32;
    private readonly int _stateLength = 80;
    private readonly string _codeChallengeMethod = "S256";

    internal string State { get; init; }
    internal string CodeVerifier { get; init; }
    internal string CodeChallenge { get; init; }
    internal string CodeChallengeMethod => _codeChallengeMethod;

    internal string RedirectUri { get; init; } = string.Empty;
    internal string ClientId { get; init; } = string.Empty;
    internal string ClientSecret { get; init; } = string.Empty;

    private Session()
    {
        State = ConvertToBase64Url(GenerateRandomBytes(_stateLength));
        CodeVerifier = ConvertToBase64Url(GenerateRandomBytes(_codeVerifierLength));
        var hashed = GetSha256Hash(Encoding.UTF8.GetBytes(CodeVerifier));
        CodeChallenge = ConvertToBase64Url(hashed);
    }

    public Session(string redirectUri, string clientId, string clientSecret) : this()
    {
        RedirectUri = redirectUri;
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    private byte[] GenerateRandomBytes(int length)
    {
        byte[] randomBytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return randomBytes;
    }

    private string ConvertToBase64Url(byte[] input )
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private byte[] GetSha256Hash(byte[] input)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(input);
    }

}
