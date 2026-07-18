namespace Medoz.Setup.Services;

/// <summary>
/// 初期セットアップ画面が扱う設定値の集合。
/// 現行の環境変数から読み込み、保存時に環境変数 (User target) と PostX/.env に書き戻す。
/// </summary>
public sealed class SetupSettings
{
    // LLM
    public string LlmProvider { get; set; } = "claude";
    public string AnthropicApiKey { get; set; } = "";
    public string ClaudeModel { get; set; } = "claude-sonnet-4-6";
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string OpenAIApiKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4o";

    // ペルソナ (空欄 = 未設定 = 同梱サンプル personas/default で起動)
    public string PersonaDir { get; set; } = "";

    // VOICEVOX
    public string VoicevoxUrl { get; set; } = "http://127.0.0.1:50021";
    public int SpeakerId { get; set; } = 3;
    public string OutputDeviceName { get; set; } = "CABLE Input";

    // 外部アプリ (Studio の起動パネルが使う起動パス)
    public string VoicevoxExePath { get; set; } = "";
    // PuruPuruPNGTuber は exe ではなくブラウザで動く Web アプリ。run_local_server.bat のパスを指す
    public string PurupuruPath { get; set; } = "";

    // X (Twitter)
    public string XConsumerKey { get; set; } = "";
    public string XConsumerSecret { get; set; } = "";
    public string XAccessToken { get; set; } = "";
    public string XAccessTokenSecret { get; set; } = "";
}
