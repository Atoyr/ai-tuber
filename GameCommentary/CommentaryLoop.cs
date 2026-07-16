using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;

namespace Medoz.GameCommentary;

/// <summary>
/// ゲーム実況の1ステップ (キャプチャ → Vision実況 → フィルタ → 発話) を担うループ本体
/// (Python版 game_commentary.py の main ループ内処理相当)。
/// LLM は <see cref="Persona"/>、キャプチャは <see cref="IWindowCapture"/>、発話は
/// <see cref="ISpeaker"/> に抽象化してあるので、フェイクを注入してテストできる。
/// </summary>
public class CommentaryLoop
{
    private readonly IWindowCapture _capture;
    private readonly Persona _persona;
    private readonly ModerationFilter _filter;
    private readonly ISpeaker _speaker;
    private readonly int _historyLimit;
    private readonly string _displayName;
    private readonly Action<string> _log;
    private readonly List<string> _history = new();

    // Vision に渡す画像のメディアタイプ (WindowCapture は JPEG を返す)
    private const string ImageMediaType = "image/jpeg";

    /// <param name="log">
    /// [error] / [skip] の診断ログの出力先。省略時は Console.WriteLine (CLI の従来挙動)。
    /// Studio はここから SSE へ流す。
    /// </param>
    public CommentaryLoop(IWindowCapture capture, Persona persona, ModerationFilter filter,
                          ISpeaker speaker, int historyLimit = 4, string displayName = "ぷる乃",
                          Action<string>? log = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _persona = persona ?? throw new ArgumentNullException(nameof(persona));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _historyLimit = historyLimit;
        _displayName = displayName;
        _log = log ?? Console.WriteLine;
    }

    /// <summary>直近の実況履歴 (最大 historyLimit 件)。</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// 1回分の実況を実行する。生成した実況文を返す。
    /// フィルタ違反・キャプチャ/生成エラー時はログを出して null を返し、履歴は汚さない
    /// (ループを殺さず、次のキャプチャで再試行できるようにする)。
    /// </summary>
    public async Task<string?> RunOnceAsync(CancellationToken ct = default)
    {
        string comment;
        try
        {
            byte[] imageBytes = _capture.CaptureJpeg();
            string base64 = Convert.ToBase64String(imageBytes);
            var image = new ImageContent(ImageMediaType, base64);
            string userText = CommentaryMessages.BuildUserText(_history, _historyLimit);
            comment = await _persona.GenerateWithImageAsync(image, userText, maxTokens: 150, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // 終了要求はそのまま伝える
        }
        catch (Exception ex)
        {
            // キャプチャ or 生成の失敗: 履歴を汚さずスキップして次へ
            _log($"[error] {ex.Message} (次のキャプチャで再試行)");
            return null;
        }

        if (!_filter.IsSafe(comment))
        {
            // フィルタ違反: 応答を破棄し履歴にも入れない
            _log($"[skip] フィルタに掛かった実況: {comment}");
            return null;
        }

        // 安全な実況のみ履歴に積む (末尾 historyLimit 件に制限)
        _history.Add(comment);
        if (_history.Count > _historyLimit)
        {
            _history.RemoveRange(0, _history.Count - _historyLimit);
        }

        Console.WriteLine($"{_displayName}: {comment}");

        try
        {
            await _speaker.SpeakAsync(comment, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 発話の失敗はログのみ。実況自体は成立しているので履歴には残す
            _log($"[error] 発話に失敗: {ex.Message}");
        }

        return comment;
    }
}
