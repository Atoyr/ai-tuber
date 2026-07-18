# 作業ステート: ゲーム実況の長文化 + 実況コンテキスト設計

作業が中断しても再開できるよう、設計・タスク・進捗をここに記録する。
(旧内容 = Phase E〜H 実装ログは 2026-07-12 に完了・コミット済みのため置き換えた。
残っていた手動テスト項目は docs/implementation-plan.md / docs/manual.md 側に記載がある)

## 依頼内容 (2026-07-18)

1. ゲーム実況で単語しか出ない → 実況らしい長文になるようにする
2. ペルソナ(人格)・知識の外部リポジトリ読み込みの現状実装を整理し、
   実況時に渡すべきコンテキストを設計する
3. テスト駆動で実装する

## 原因分析(調査済み)

- `GameCommentary/CommentaryLoop.cs` の `RunOnceAsync` が `maxTokens: 150` を**ハードコード**
  (Python版 `game_commentary.py` の値をそのまま移植)。日本語で 150 出力トークンはかなり短く、
  Gemini 系プロバイダでは thinking トークンがこの上限を食うためさらに短くなる/途切れる
- `game_system.md`(default / potofu 両ペルソナ)が「明るく**簡潔に、1〜2文**の日本語で」と
  指示しており、モデルが最短応答を選ぶ
- 実況コンテキストに「何のゲームか・どんなゲームか」の知識が無く、画面から読み取れる
  断片的な単語しか話す材料が無い

## 設計

### 実況1回あたりのコンテキスト構成(今回の設計)

```
system: character.md(人格・口調・感情タグ規約)
        + game_system.md(実況ルール: 長さ・構成・繰り返し回避)
        + knowledge/<name>.md(ゲーム知識。任意。--game / GAME_KNOWLEDGE で選択)
user:   直近の実況 最大4件(繰り返し回避の文脈)+ 実況依頼
image:  対象ウィンドウのキャプチャ(幅800px JPEG q80)
maxTokens: COMMENTARY_MAX_TOKENS(既定 500。従来 150 固定)
```

### ペルソナパッケージ契約の拡張(後方互換・任意)

`<persona-dir>/knowledge/*.md` を追加。1ファイル = 1ゲーム(または1トピック)の知識。
ゲームのルール・登場人物・用語・実況で触れると良いポイントなどを書く。
エンジンは `--game <name>` / 環境変数 `GAME_KNOWLEDGE`(Studio は start リクエストの `game`)で
`knowledge/<name>.md` を選択し、システムプロンプト末尾に結合する。
未指定なら従来どおり(knowledge ディレクトリが無くてもよい)。
指定したのに無い場合は、利用可能な知識ファイル一覧付きで fail fast(既存の UX 方針)。

### 変更点一覧

| ファイル | 変更 |
|---|---|
| `AiTuber.Core/PersonaPackage.cs` | `ListKnowledge()` / `LoadKnowledge(name)` / `BuildSystemPrompt(modeFile, knowledgeName)` 追加 |
| `AiTuber.Core/Persona.cs` | コンストラクタに `knowledgeName` 任意引数 |
| `AiTuber.Core/AppConfig.cs` | `CommentaryMaxTokens`(env `COMMENTARY_MAX_TOKENS`, 既定500)/ `GameKnowledge`(env `GAME_KNOWLEDGE`)追加 |
| `GameCommentary/CommentaryLoop.cs` | maxTokens をコンストラクタ注入(既定500) |
| `GameCommentary/Program.cs` | `--game <name>` フラグ + maxTokens 配線 |
| `Studio/Program.cs` | `CommentaryStartRequest` に `Game` 追加 |
| `Studio/Commentary/CommentarySessionHost.cs` | `Start(window, game)` で knowledge 付き Persona 構築 + maxTokens 配線 |
| `Studio/wwwroot/index.html` `app.js` | 実況パネルにゲーム知識名の入力欄(任意) |
| `personas/default/game_system.md` | 長文実況用に書き換え(3〜5文、構成指示) |
| `personas/default/knowledge/sample-game.md` | 知識ファイルの実例(生きた契約ドキュメント) |
| `../ai-tuber-potofu/game_system.md` | 同様に書き換え(**別リポジトリ。commit/push はユーザー**) |
| `docs/persona-architecture.md` | knowledge/ 契約を追記 |
| `docs/architecture.md` | 動作仕様表の実況 maxTokens 等を更新 |
| `docs/studio-architecture.md` | /api/commentary/start の body に game 追記 |

## テスト計画(TDD: 先にテスト → 実装)

- `AiTuber.Core.Tests/PersonaPackageTests`
  - knowledge あり: `LoadKnowledge("mygame")` が内容を返す
  - `BuildSystemPrompt("game_system.md", "mygame")` が知識を末尾に結合
  - knowledgeName 未指定/null: 従来と同一出力(後方互換)
  - 指定したのに無い: 利用可能一覧付き `PersonaLoadException`
  - knowledge ディレクトリ自体が無い: 明確なメッセージで例外 / `ListKnowledge()` は空
- `AiTuber.Core.Tests/PersonaTests`
  - knowledgeName 付き Persona の SystemPrompt に知識が含まれる
- `AiTuber.Core.Tests/AppConfigTests`
  - 既定値 `CommentaryMaxTokens == 500`、env `COMMENTARY_MAX_TOKENS` / `GAME_KNOWLEDGE` の読み込み
- `GameCommentary.Tests/CommentaryLoopTests`(+ FakeChatClient に maxTokens 記録を追加)
  - 既定で maxTokens 500 が IChatClient に渡る
  - コンストラクタ指定した maxTokens が渡る

## 進捗チェックリスト

- [x] 現状調査(原因特定・関連コード読了)
- [x] 設計(本ファイル)
- [x] テスト追加(red 確認済み: 13 コンパイルエラー)
- [x] AiTuber.Core 実装(PersonaPackage.ListKnowledge/LoadKnowledge/BuildSystemPrompt 拡張、Persona knowledgeName、AppConfig CommentaryMaxTokens/GameKnowledge)
- [x] GameCommentary 実装(CommentaryLoop maxTokens 注入、Program --game)
- [x] Studio 実装(SessionHost Start(window, game) + maxTokens、API body Game、UI「ゲーム知識」入力欄)
- [x] personas/default の game_system.md 書き換え + knowledge/sample-game.md 追加
- [x] ../ai-tuber-potofu の game_system.md 書き換え(**未コミット。ユーザーが commit/push すること**)
- [x] docs 更新(persona-architecture / architecture / studio-architecture / manual)
- [x] `dotnet test` 全 327 件パス(2026-07-18)
- [x] 実機確認: Studio 起動 → UI に「ゲーム知識」欄表示、`POST /api/commentary/start` が
      `{window, game}` を受理、存在しない知識名は利用可能一覧付き 400(fail fast)を確認

## 完了(2026-07-18)

実装・テスト・ドキュメントすべて完了。残りはユーザー作業のみ:

1. ai-tuber-potofu リポジトリの game_system.md 変更を commit/push
2. (任意)実況したいゲームの知識ファイルをぽとふリポジトリに追加
   (`knowledge/<名前>.md`)し、`--game <名前>` で起動して実況の長さ・質を実機確認
3. 本リポジトリの変更をコミット(エンジン側)

発見した別件(チップとして提案済み): CommentarySessionHost.Start の失敗パスで
生成済み WGC capture が Dispose されない(既存バグ・今回のスコープ外)。

---

# 追加タスク: README 記載 + マニュアルの GitHub Pages 公開 (2026-07-18)

- [x] README 更新: ゲーム実況の長文化・ゲーム知識 (`--game` / `knowledge/`) の記載、
      マニュアル Web 版 (https://atoyr.github.io/ai-tuber/) へのリンク追加
- [x] `docs/_config.yml` + `docs/index.md` 追加(Jekyll。node 不使用の方針どおり)
- [x] `.github/workflows/pages.yml` 追加(docs/ を actions/jekyll-build-pages で公開。
      ai-tuber-blogs と同じ「Pages Source = GitHub Actions」方式)
- [x] ブランチ `feat/game-commentary-context-and-docs-pages` に2コミット
      (実況機能 26a291d / Pages+README b9a7542)→ push → PR #4 作成
      https://github.com/Atoyr/ai-tuber/pull/4
- [x] Pages を build_type=workflow で有効化済み(gh api。URL: https://atoyr.github.io/ai-tuber/)
- [ ] PR #4 マージ後に pages.yml が走り https://atoyr.github.io/ai-tuber/ が公開される(**マージはユーザー**。
      マージ後 Actions の「Deploy docs to GitHub Pages」が成功するか確認すること)

## メモ(再開時に読む)

- 挙動の正解は reference/python_v2 だが、**今回は意図的に Python 版から乖離する**
  (150 トークン → 500、1〜2文 → 3〜5文)。docs/architecture.md の動作仕様表を更新して正とする
- potofu ペルソナは sibling の `C:\Users\real-\src\github.com\atoyr\ai-tuber-potofu`(private repo)。
  エンジン側の変更とは別に、あちらのリポジトリで commit が必要
- Studio の実況は Live セッションと 409 相互排他(既存仕様のまま)
