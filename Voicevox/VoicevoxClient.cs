namespace Medoz.Voicevox;

/// <summary>
/// VOICEVOX ローカル API クライアント。
/// /audio_query → /synthesis の2段階で wav バイト列を生成する。
/// </summary>
public class VoicevoxClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public VoicevoxClient(string baseUrl = "http://127.0.0.1:50021", HttpClient? httpClient = null)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new ArgumentNullException(nameof(baseUrl), "Base URL cannot be null or empty");
        }
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// テキストを音声合成して wav バイト列を返す。
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, int speakerId = 3, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentNullException(nameof(text), "Text cannot be null or empty");
        }

        var queryUrl = $"{_baseUrl}/audio_query?text={Uri.EscapeDataString(text)}&speaker={speakerId}";
        using var queryResponse = await _httpClient.PostAsync(queryUrl, null, cancellationToken);
        queryResponse.EnsureSuccessStatusCode();
        var audioQueryJson = await queryResponse.Content.ReadAsStringAsync(cancellationToken);

        var synthesisUrl = $"{_baseUrl}/synthesis?speaker={speakerId}";
        using var synthesisContent = new StringContent(audioQueryJson, System.Text.Encoding.UTF8, "application/json");
        using var synthesisResponse = await _httpClient.PostAsync(synthesisUrl, synthesisContent, cancellationToken);
        synthesisResponse.EnsureSuccessStatusCode();

        return await synthesisResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
