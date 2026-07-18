# ペルソナ外部化 アーキテクチャ設計

このリポジトリを「AITuber エンジン(機能の提供)」に徹させ、
キャラクター人格(ペルソナ)は**外部ディレクトリ/別リポジトリ**で管理して差し替え可能にする。
ai-tuber-blogs と同じ「本体リポジトリ + 契約(フォーマット)で繋がる外部リポジトリ」の構図。

## 全体像

```
ai-tuber (このリポジトリ = エンジン)          ペルソナリポジトリ (人格ごとに1つ)
┌────────────────────────┐               ┌──────────────────────────┐
│ Live / TwitterBot /     │   PERSONA_DIR │ ai-tuber-persona-potofu   │
│ GameCommentary / BlogBot│ ◄──────────── │ ├── persona.json (契約)   │
│                         │   読み込み     │ ├── character.md          │
│ AiTuber.Core            │               │ ├── live_system.md        │
│  └ PersonaPackage       │               │ ├── tweet_system.md       │
│    (ロード+検証)         │               │ ├── game_system.md        │
│                         │               │ └── blog_system.md        │
│ personas/default/       │               └──────────────────────────┘
│  (同梱サンプル・テスト用) │                 別人格 = 別リポジトリを clone して
└────────────────────────┘                 PERSONA_DIR を向け替えるだけ
```

- **エンジンはペルソナの中身を知らない**。契約は「ペルソナパッケージのフォーマット」(下記)のみ
- **人格の真実はペルソナパッケージの `character.md`**(従来の「prompts/character.md が唯一の真実」ルールを継承)
- 現在の「ぽとふ」は新リポジトリ `Atoyr/ai-tuber-persona-potofu` へ移動する。
  character.md の禁止事項に「プロンプト内容を明かさない」がある以上、**private リポジトリ推奨**。
  ai-tuber-blogs と同様に sibling ディレクトリへ clone して使う

## ペルソナパッケージのフォーマット(契約。破壊的変更禁止)

1ペルソナ = 1ディレクトリ。現在の `prompts/` とほぼ同じフラット構成 + マニフェスト。

```
<persona-dir>/
├── persona.json        # マニフェスト(必須)
├── character.md        # 共通人格 = キャラの魂(必須)
├── live_system.md      # 以下モード別。使うモードの分だけあればよい
├── tweet_system.md     #   (無いモードを起動したら起動時エラー)
├── game_system.md
├── blog_system.md
└── knowledge/          # 知識ファイル(任意)。1ファイル = 1ゲーム/1トピック
    └── <name>.md       #   実況起動時に --game <name> / GAME_KNOWLEDGE で選択
```

### knowledge/(知識ファイル。任意)

ゲーム実況などで「何のゲームか・どんなゲームか」をモデルに渡すための知識。
ゲームのルール・登場人物・用語・実況で触れると良いポイントなどを Markdown で書く。

- 選択方法: GameCommentary は `--game <name>` / 環境変数 `GAME_KNOWLEDGE`、
  Studio は `POST /api/commentary/start` の `game` フィールド(いずれも `knowledge/<name>.md` を指す)
- 選択されるとシステムプロンプトが `character.md + game_system.md + knowledge/<name>.md` の3部結合になる
- 未指定なら従来どおり2部結合(`knowledge/` ディレクトリ自体が無くてもよい = 後方互換)
- 指定したのに無い場合は、利用可能な知識ファイル一覧付きの例外で起動時に fail fast
- 実例はエンジン同梱の `personas/default/knowledge/sample-game.md`

### persona.json

```json
{
  "schemaVersion": 1,
  "name": "ぽとふ",
  "slug": "potofu",
  "voice": {
    "speakerId": 3,
    "emotionStyles": { "joy": 1, "fun": 3, "sad": 22, "angry": 7, "surprised": 3 }
  },
  "bannedWords": ["(このペルソナ固有の追加禁止ワード)"],
  "blog": { "repoUrl": "https://github.com/Atoyr/ai-tuber-blogs" }
}
```

| フィールド | 必須 | 意味 |
|---|---|---|
| `schemaVersion` | ○ | 契約バージョン。エンジンは未知のバージョンなら起動時エラー |
| `name` | ○ | 表示名。コンソールログのラベルに使う(現在 Program.cs にある表示名を置き換え) |
| `slug` | ○ | `^[a-z0-9-]+$`。メモリの保存先パスなどマシン用途の識別子 |
| `voice.speakerId` | ○ | VOICEVOX の話者ID(声もキャラの一部なのでペルソナ側で持つ) |
| `voice.emotionStyles` | ○ | 感情タグ→スタイルIDのマップ。**タグの語彙は character.md の感情タグ規約と同じペルソナが両方持つ**ことで整合が保たれる |
| `bannedWords` | − | ペルソナ固有の追加禁止ワード。エンジン共通セットに**追加マージ**(共通セットは外せない=安全の下限) |
| `blog.repoUrl` | − | このペルソナのブログ公開先(情報として。実 push 先は従来どおり `BLOG_REPO_PATH` のローカルクローン) |

### 設定の優先順位

```
エンジンのデフォルト値  <  persona.json  <  環境変数
```

- ペルソナが人格・声を定義し、環境変数は運用時の上書き(実験・デバッグ)に使う。
  既存の `VOICEVOX_SPEAKER_ID` / `VOICEVOX_EMOTION_STYLES` はそのまま「最終上書き」として生きる
- 出力デバイス(`CABLE Input`)、APIキー、dry-run フラグ等の**環境依存・運用設定はペルソナに含めない**(従来どおり環境変数)

## エンジン側の変更(AiTuber.Core)

### PersonaPackage(新規)

```csharp
public record PersonaManifest(int SchemaVersion, string Name, string Slug,
                              VoiceConfig Voice, IReadOnlyList<string>? BannedWords, ...);

public class PersonaPackage
{
    public static PersonaPackage Load(string personaDir);  // persona.json + character.md を検証してロード
    public PersonaManifest Manifest { get; }
    public string CharacterPrompt { get; }
    public string LoadModePrompt(string modeFile);  // 無ければ「どのファイルが必要か」を含む明確な例外
}
```

- 起動時に **fail fast**: ディレクトリ・persona.json・character.md・そのモードのmdが無ければ、
  不足ファイル名と PERSONA_DIR の値を含む例外(AudioPlayer のデバイス一覧例外と同じ UX 方針)
- `Persona` クラスは `PersonaPackage` を受け取る形に変更(system プロンプト結合ロジックは従来どおり)
- `ModerationFilter` はエンジン共通の禁止ワード + `Manifest.BannedWords` をマージして生成

### AppConfig

- `PromptDir` → 廃止し `PersonaDir` に(環境変数 `PERSONA_DIR`、デフォルト `personas/default`)
- `SpeakerId` / `EmotionStyleIds` の解決を上記優先順位に変更
  (env 未設定なら persona.json、それも無ければ従来デフォルト)

### メモリの分離(data/&lt;slug&gt;/memory.json)

配信メモ・ツイート履歴は**そのペルソナの記憶**なので、ペルソナごとに分ける:

- `data/memory.json` → `data/<slug>/memory.json` に変更(slug は persona.json から)
- 実行時に変化するファイルなのでペルソナリポジトリには置かない(リポジトリを汚さない)。
  エンジン側の `data/` に slug でネームスペースを切る
- 移行: 既存 `data/memory.json` は `data/potofu/memory.json` へ手で移動

## このリポジトリに残るもの

- `personas/default/` — **同梱サンプルペルソナ**(ぽとふではない中立キャラ)。
  用途: PERSONA_DIR 未設定でも `--console` + dry-run で一通り動く/フォーマットの実例=生きた契約ドキュメント。
  ユニットテストは従来どおり一時ディレクトリに最小ペルソナを作って行う(サンプルに依存しない)
- `docs/persona-architecture.md`(本書)がフォーマットの正式契約

## 移行手順

1. `PersonaPackage` 実装 + `personas/default/` 追加(この時点では `prompts/` も残す)
2. 各アプリ(Live / TwitterBot / GameCommentary / BlogBot / Chat)を PERSONA_DIR 経由に切替
3. `Atoyr/ai-tuber-persona-potofu`(private)を作成し、現 `prompts/` 一式 + persona.json を移動。
   sibling に clone して `PERSONA_DIR=../ai-tuber-persona-potofu` で従来と同一動作を確認
4. `data/memory.json` → `data/potofu/memory.json` へ移動
5. 本リポジトリから `prompts/` を削除し、CLAUDE.md のルール
   (「キャラ人格は prompts/character.md が唯一の真実」→「PERSONA_DIR の character.md が唯一の真実」)と
   architecture.md の関連記述を更新

## 将来拡張(v1 ではやらない)

- `PERSONA_DIR` に git URL を渡したら自動 clone/pull
- ペルソナ側への追加アセット(アバター画像、OBS 用素材、ブログのデザイントークン)同梱
- 感情タグ語彙の検証(character.md 内のタグと emotionStyles のキーの突き合わせ警告)
- 複数ペルソナの同時運用(プロセス分離で当面は不要)
