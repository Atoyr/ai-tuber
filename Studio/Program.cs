using System.Diagnostics;
using System.Text.Json;
using Medoz.AiTuber.Core;
using Medoz.GameCommentary;
using Medoz.Live;
using Medoz.Studio;
using Medoz.Studio.Apps;
using Medoz.Studio.Commentary;
using Medoz.Studio.Events;
using Medoz.Studio.LiveHosting;
using Medoz.Studio.Settings;
using Medoz.Voicevox;

// AITuber Studio (配信コントロールパネル / ローカル Web UI)。設計: docs/studio-architecture.md
// - http://127.0.0.1:5100 固定 (localhost のみ・認証なし・LAN 非公開)。ポートは STUDIO_PORT で変更可
// - 会話ログ閲覧 (SSE)・設定変更 (data/studio.json)・VOICEVOX / PuruPuruPNGTuber の起動・配信の開始/停止
// - 外部投稿はしない (発話・プロセス起動のみ)
//
// 実行:
//   dotnet run --project Studio            # ブラウザで http://127.0.0.1:5100 を開く
//   dotnet run --project Studio -- --open  # 既定ブラウザを自動で開く

int port = int.TryParse(Environment.GetEnvironmentVariable("STUDIO_PORT"), out int p) ? p : 5100;
string listenUrl = $"http://127.0.0.1:{port}";

// wwwroot は出力ディレクトリへコピーされたものを使う (作業ディレクトリはリポジトリルート想定のため)
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});
builder.WebHost.UseUrls(listenUrl);
builder.Logging.SetMinimumLevel(LogLevel.Warning); // コンソールは会話ログ主体にする

// --- サービス構築 (ハンドラは薄く、ロジックはサービスクラスに置く) ---
var settingsStore = new StudioSettingsStore();
var broker = new EventBroker();

// LiveEvent → SSE。SessionStateChanged は state イベントとして別経路で合成して流す
StudioStateNotifier? notifier = null;
var liveHost = new LiveSessionHost(settingsStore, liveEvent =>
{
    var sseEvent = SseEventMapper.Map(liveEvent);
    if (sseEvent is not null)
    {
        broker.Publish(sseEvent);
    }
    else if (liveEvent is SessionStateChanged)
    {
        notifier?.PublishIfChanged();
    }
});
// ゲーム実況イベント → SSE (発話は commentary、診断は log、状態変化は state)
var commentaryHost = new CommentarySessionHost(settingsStore, commentaryEvent =>
{
    switch (commentaryEvent)
    {
        case CommentarySpoken spoken:
            broker.Publish(SseEventMapper.Commentary(spoken.At, spoken.Text));
            break;
        case CommentaryLogMessage log:
            broker.Publish(SseEventMapper.Log(log.At, log.Level, log.Message));
            break;
        case CommentaryStateChanged:
            notifier?.PublishIfChanged();
            break;
    }
});
var launcher = new AppLauncher(new ProcessRunner(), AppConfig.LoadFromEnvironment);
notifier = new StudioStateNotifier(broker, launcher, liveHost, commentaryHost);

builder.Services.AddSingleton(settingsStore);
builder.Services.AddSingleton(broker);
builder.Services.AddSingleton(liveHost);
builder.Services.AddSingleton(commentaryHost);
builder.Services.AddSingleton(launcher);
builder.Services.AddSingleton(notifier);

var app = builder.Build();

app.UseDefaultFiles();  // / → wwwroot/index.html
app.UseStaticFiles();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// AppLauncher の操作を包む共通ハンドラ (成功: 200 + status、失敗: 400 + {"error": ...})
async Task<IResult> RunAppAction(Func<Task> action)
{
    try
    {
        await action();
        notifier?.PublishIfChanged();
        return Results.Json(await BuildStatusAsync(), jsonOptions);
    }
    catch (Exception ex)
    {
        notifier?.PublishIfChanged();
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "error", ex.Message));
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: 400);
    }
}

// GET /api/status 用の合成ステータス
async Task<object> BuildStatusAsync()
{
    await launcher.RefreshAllAsync(CancellationToken.None);
    var vv = launcher.Voicevox.Current();
    var pp = launcher.Purupuru.Current();
    var live = liveHost.Status();
    var commentary = commentaryHost.Status();

    return new
    {
        voicevox = new { state = vv.State.ToString(), version = vv.Version },
        purupuru = new { state = pp.State.ToString(), url = launcher.PurupuruUrl },
        live = new
        {
            state = live.State,
            source = live.Source,
            persona = live.PersonaName,
            startedAt = live.StartedAt,
        },
        commentary = new
        {
            state = commentary.State,
            window = commentary.Window,
            startedAt = commentary.StartedAt,
        },
    };
}

// --- ステータス ---
app.MapGet("/api/status", async () =>
{
    var status = await BuildStatusAsync();
    notifier?.PublishIfChanged();
    return Results.Json(status, jsonOptions);
});

// --- 外部アプリの起動・停止 ---
app.MapPost("/api/apps/{name}/start", (string name) =>
    name is "voicevox" or "purupuru"
        ? RunAppAction(() => launcher.Get(name).StartAsync(CancellationToken.None))
        : Task.FromResult(Results.Json(new { error = $"未知のアプリです: {name}" }, jsonOptions, statusCode: 404)));

app.MapPost("/api/apps/{name}/stop", (string name) =>
    name is "voicevox" or "purupuru"
        ? RunAppAction(() => launcher.Get(name).StopAsync())
        : Task.FromResult(Results.Json(new { error = $"未知のアプリです: {name}" }, jsonOptions, statusCode: 404)));

app.MapPost("/api/apps/start-all", () =>
    RunAppAction(() => launcher.StartAllAsync(CancellationToken.None)));

// --- 配信セッション ---
app.MapPost("/api/live/start", (LiveStartRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request?.Source))
    {
        return Results.Json(new { error = "source (manual / twitch / youtube) を指定してください。" }, jsonOptions, statusCode: 400);
    }
    if (commentaryHost.Status().State == "Running")
    {
        return Results.Json(new { error = "ゲーム実況の実行中は配信セッションを開始できません (発話が重なるため)。先に実況を停止してください。" }, jsonOptions, statusCode: 409);
    }
    try
    {
        var status = liveHost.Start(request.Source, request.Target);
        notifier?.PublishIfChanged();
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "info", $"配信セッションを開始しました (source: {status.Source})"));
        return Results.Json(new { state = status.State, source = status.Source, startedAt = status.StartedAt }, jsonOptions);
    }
    catch (LiveSessionHostException ex)
    {
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "error", ex.Message));
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: ex.StatusCode);
    }
});

app.MapPost("/api/live/stop", async () =>
{
    try
    {
        var status = await liveHost.StopAsync(); // 配信メモ保存まで待つ (Ctrl+C 相当)
        notifier?.PublishIfChanged();
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "info", "配信セッションを停止しました"));
        return Results.Json(new { state = status.State }, jsonOptions);
    }
    catch (LiveSessionHostException ex)
    {
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: ex.StatusCode);
    }
});

app.MapPost("/api/live/comment", (CommentRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request?.Text))
    {
        return Results.Json(new { error = "text を指定してください。" }, jsonOptions, statusCode: 400);
    }
    try
    {
        liveHost.InjectComment(string.IsNullOrWhiteSpace(request.Author) ? "テスト" : request.Author, request.Text);
        return Results.Json(new { ok = true }, jsonOptions);
    }
    catch (LiveSessionHostException ex)
    {
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: ex.StatusCode);
    }
});

// --- ゲーム実況セッション (ウィンドウキャプチャ → Vision実況 → 発話) ---
app.MapGet("/api/windows", () =>
{
    try
    {
        return Results.Json(new { windows = WindowCapture.GetVisibleWindowTitles() }, jsonOptions);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message, windows = Array.Empty<string>() }, jsonOptions, statusCode: 500);
    }
});

app.MapPost("/api/commentary/start", (CommentaryStartRequest request) =>
{
    // 配信セッションと実況の同時実行は発話が重なるため相互排他にする
    string liveState = liveHost.Status().State;
    if (liveState is nameof(SessionState.Starting) or nameof(SessionState.Running))
    {
        return Results.Json(new { error = "配信セッションの実行中はゲーム実況を開始できません (発話が重なるため)。先に配信を停止してください。" }, jsonOptions, statusCode: 409);
    }
    try
    {
        var status = commentaryHost.Start(request?.Window, request?.Game);
        notifier?.PublishIfChanged();
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "info", $"ゲーム実況を開始しました (対象: {status.Window})"));
        return Results.Json(new { state = status.State, window = status.Window, startedAt = status.StartedAt }, jsonOptions);
    }
    catch (LiveSessionHostException ex)
    {
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "error", ex.Message));
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: ex.StatusCode);
    }
});

app.MapPost("/api/commentary/stop", async () =>
{
    try
    {
        var status = await commentaryHost.StopAsync();
        notifier?.PublishIfChanged();
        broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "info", "ゲーム実況を停止しました"));
        return Results.Json(new { state = status.State }, jsonOptions);
    }
    catch (LiveSessionHostException ex)
    {
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: ex.StatusCode);
    }
});

// --- 設定 ---
// AppConfig が参照するシークレット環境変数。値は絶対に返さない ("set"/"unset" のみ)
string[] secretEnvNames =
[
    "ANTHROPIC_API_KEY", "GEMINI_API_KEY", "OPENAI_API_KEY", "YOUTUBE_API_KEY",
    "X_API_KEY", "X_API_SECRET", "X_ACCESS_TOKEN", "X_ACCESS_SECRET",
];

app.MapGet("/api/settings", () =>
{
    var effective = liveHost.EffectiveSettings();
    var secrets = secretEnvNames.ToDictionary(
        name => name,
        name => Environment.GetEnvironmentVariable(name) is { Length: > 0 } ? "set" : "unset");

    return Results.Json(new
    {
        immediate = new
        {
            commentBatchSec = effective.CommentBatchSec,
            freetalkAfterSec = effective.FreetalkAfterSec,
            freetalkEnabled = effective.FreetalkEnabled,
            speakerId = effective.SpeakerId,
            paused = effective.Paused,
            captureIntervalSec = effective.CaptureIntervalSec,
            commentaryTimingMode = effective.CommentaryTimingMode,
            commentaryAfterSpeechSec = effective.CommentaryAfterSpeechSec,
        },
        nextSession = new
        {
            source = effective.Source,
            target = effective.Target,
            outputDevice = effective.OutputDevice,
            llmProvider = effective.LlmProvider,
            llmModel = effective.LlmModel,
            personaDir = effective.PersonaDir,
        },
        secrets,
    }, jsonOptions);
});

app.MapPut("/api/settings", (SettingsPatch patch) =>
{
    if (patch is null || (patch.Immediate is null && patch.NextSession is null))
    {
        return Results.Json(new { error = "変更する項目を immediate / nextSession のいずれかに指定してください。" }, jsonOptions, statusCode: 400);
    }

    // UI で触った項目だけを studio.json に保存する (null は未変更 = 保存しない)
    settingsStore.Update(data =>
    {
        if (patch.Immediate is { } im)
        {
            if (im.CommentBatchSec is double batch) { data.CommentBatchSec = batch; }
            if (im.FreetalkAfterSec is double freetalk) { data.FreetalkAfterSec = freetalk; }
            if (im.FreetalkEnabled is bool enabled) { data.FreetalkEnabled = enabled; }
            if (im.SpeakerId is int speakerId) { data.SpeakerId = speakerId; }
            if (im.Paused is bool paused) { data.Paused = paused; }
            // 実況の間隔設定。実行中の実況ループは待ち時間の計算ごとに studio.json を読み直すため
            // ApplyImmediate のような push は不要 (保存だけで次の待ちから反映される)
            if (im.CaptureIntervalSec is double captureInterval) { data.CaptureIntervalSec = captureInterval; }
            if (im.CommentaryTimingMode is not null) { data.CommentaryTimingMode = CommentaryTiming.Normalize(im.CommentaryTimingMode); }
            if (im.CommentaryAfterSpeechSec is double afterSpeech) { data.CommentaryAfterSpeechSec = afterSpeech; }
        }
        if (patch.NextSession is { } next)
        {
            if (next.Source is not null) { data.Source = next.Source; }
            if (next.Target is not null) { data.Target = next.Target; }
            if (next.OutputDevice is not null) { data.OutputDevice = next.OutputDevice; }
            if (next.LlmProvider is not null) { data.LlmProvider = next.LlmProvider; }
            if (next.LlmModel is not null) { data.LlmModel = next.LlmModel; }
            if (next.PersonaDir is not null) { data.PersonaDir = next.PersonaDir; }
        }
    });

    // 即時反映系は実行中セッションへも反映
    if (patch.Immediate is { } immediate)
    {
        liveHost.ApplyImmediate(immediate);
    }
    return Results.Json(new { ok = true }, jsonOptions);
});

// --- 出力デバイス一覧 ---
app.MapGet("/api/devices", () =>
{
    try
    {
        return Results.Json(new { devices = AudioPlayer.GetOutputDeviceNames() }, jsonOptions);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message, devices = Array.Empty<string>() }, jsonOptions, statusCode: 500);
    }
});

// --- 配信画面 (OBS ブラウザソース用オーバーレイ) ---
// http://127.0.0.1:5100/overlay を OBS のブラウザソースに指定する。
// アバターは PuruPuruPNGTuber の OBS モード (mode=obs&transparent=1) を iframe で埋め込む。
app.MapGet("/overlay", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath, "overlay.html"), "text/html; charset=utf-8"));

app.MapGet("/api/overlay/config", () =>
{
    // PuruPuru の OBS モード URL。control ページの「OBS用URL」と同じ形式にする
    string avatarUrl = $"{launcher.PurupuruUrl}/?mode=obs&transparent=1";
    return Results.Json(new { avatarUrl, overlayUrl = $"{listenUrl}/overlay" }, jsonOptions);
});

// --- SSE (直近200件リプレイ + 追記配信。?replay=0 でリプレイ無し) ---
app.MapGet("/api/events", async (HttpContext context) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    // 配信画面は過去ログを画面に出したくないので replay=0 で接続する
    bool replay = context.Request.Query["replay"] != "0";

    try
    {
        await foreach (var sseEvent in broker.SubscribeAsync(context.RequestAborted, replay))
        {
            await context.Response.WriteAsync(sseEvent.ToWireFormat(), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        // クライアント切断で綺麗に外れる
    }
});

// --- 起動 ---
broker.Publish(SseEventMapper.Log(DateTimeOffset.Now, "info", $"Studio を起動しました: {listenUrl}"));
Console.WriteLine($"AITuber Studio: {listenUrl} (Ctrl+C で終了)");

if (args.Contains("--open"))
{
    // 既定ブラウザで開く (Windows)
    try
    {
        Process.Start(new ProcessStartInfo { FileName = listenUrl, UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ブラウザを開けませんでした: {ex.Message}");
    }
}

// 終了時は配信セッション (配信メモ保存まで) とゲーム実況を止める
app.Lifetime.ApplicationStopping.Register(() =>
{
    liveHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
    commentaryHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

app.Run();

/// <summary>POST /api/live/start の body。</summary>
public sealed record LiveStartRequest(string? Source, string? Target);

/// <summary>POST /api/live/comment の body。</summary>
public sealed record CommentRequest(string? Author, string? Text);

/// <summary>POST /api/commentary/start の body。</summary>
/// <summary>Game はペルソナの knowledge/&lt;name&gt;.md を実況コンテキストに使う任意指定。</summary>
public sealed record CommentaryStartRequest(string? Window, string? Game);
