# ブログ機能 アーキテクチャ設計

キャラクター「ぽとふ」のブログを GitHub Pages で公開する。
サイト本体は別リポジトリ [Atoyr/ai-tuber-blogs](https://github.com/Atoyr/ai-tuber-blogs)、
記事の生成・push は本リポジトリの `BlogBot/` が担う。

## 全体像

```
ai-tuber (このリポジトリ)                    ai-tuber-blogs (公開リポジトリ)
┌──────────────────┐                      ┌──────────────────────────┐
│ BlogBot           │  content/posts/      │ Nuxt 3 + @nuxt/content    │
│  Claude(人格) →   │  YYYY-MM-DD-slug.md  │                           │
│  検証(フィルタ等) → │ ── git push main ──> │ GitHub Actions:           │
│  md組立 → push    │                      │  push → nuxt generate     │
└──────────────────┘                      │  → GitHub Pages 公開       │
                                           └──────────────────────────┘
                    公開URL: https://atoyr.github.io/ai-tuber-blogs/
```

- **記事の真実は markdown ファイル**。`content/posts/` に置いて main に push すれば公開される。
  人間が手で記事を書いて push しても同じように公開される(ボット専用の仕組みにしない)
- サイト側(ai-tuber-blogs)は記事フォーマット(下記 frontmatter)だけを契約とし、
  生成側(BlogBot)の実装を知らない

## ai-tuber-blogs リポジトリ(Nuxt サイト)

### 技術構成

| 項目 | 選定 | 理由 |
|---|---|---|
| フレームワーク | Nuxt 3 (最新安定版) | 要件指定。SSG で GitHub Pages に載る |
| コンテンツ | `@nuxt/content` v3 | `content/posts/*.md` をコレクションとして型付きで扱える |
| ビルド | `nuxt generate` (SSG) | 全記事を静的HTML化。サーバ不要 |
| デプロイ | GitHub Actions → `actions/deploy-pages` | main への push で自動公開 |
| スタイル | 素の CSS または軽量CSS(サイト内に閉じる) | 依存を増やさない |

### ディレクトリ構成

```
ai-tuber-blogs/
├── CLAUDE.md               # サイト開発用の指示(記事フォーマット契約を含む)
├── nuxt.config.ts          # baseURL: /ai-tuber-blogs/ (NUXT_APP_BASE_URL で上書き可)
├── content.config.ts       # posts コレクション定義 + frontmatter スキーマ(zod)
├── content/
│   └── posts/
│       └── YYYY-MM-DD-<slug>.md   # 記事。BlogBot または人間が追加する
├── app/ (または直下)
│   ├── pages/
│   │   ├── index.vue       # 記事一覧(新しい順)
│   │   ├── posts/[slug].vue # 記事詳細
│   │   └── about.vue       # ぽとふのプロフィール
│   └── components/         # PostCard など
├── public/                 # favicon, OGP画像
└── .github/workflows/deploy.yml
```

### 記事フォーマット(BlogBot との契約。破壊的変更禁止)

ファイル名: `content/posts/YYYY-MM-DD-<slug>.md`(slug は `[a-z0-9-]+`)

```markdown
---
title: 記事タイトル(40字以内)
description: 一覧・メタタグ用の要約(80字程度)
date: 2026-07-12T21:00:00+09:00
kind: daily            # daily | stream | game | announce
tags: [スープ, 配信]
---

本文(markdown)。見出しは h2 (##) から使う。
```

- `kind` の意味: `daily`=日常ブログ / `stream`=配信ふりかえり / `game`=ゲームの話 / `announce`=お知らせ
- 一覧は `date` 降順。`draft: true` を付けた記事はビルドから除外する

### デプロイ (GitHub Actions)

```yaml
# .github/workflows/deploy.yml の要点
on: { push: { branches: [main] }, workflow_dispatch: {} }
permissions: { contents: read, pages: write, id-token: write }
steps:
  - checkout → setup-node (Node 22, cache: npm) → npm ci
  - NUXT_APP_BASE_URL=/ai-tuber-blogs/ npx nuxt generate
  - actions/upload-pages-artifact (path: .output/public)
  - actions/deploy-pages
```

**初回のみ手動設定**: リポジトリ Settings → Pages → Source を「GitHub Actions」にする。

### デザイン方針

- ぽとふのキャラに合わせて明るく・あたたかい配色(スープ/ぽとふ→暖色オレンジ系)
- 一人称のブログとして成立する見た目(企業ブログ風にしない)。日本語で読みやすい行間・フォント指定
- レスポンシブ対応。ダークモードは任意

## BlogBot(このリポジトリ・C#)

TwitterBot と同じ構造で作る。プロジェクト `BlogBot/`(名前空間 `Medoz.BlogBot`)+ `BlogBot.Tests/`。

### パイプライン

```
コンテキスト組立(日時・直近の配信メモ・直近ツイート・直近ブログ記事タイトル)
  → Persona(character.md + blog_system.md) で生成
  → JSONパース {"title","slug","description","kind","tags","body"}
  → 検証(下表)。失敗したら再生成(最大3回)、全滅ならスキップ
  → frontmatter + 本文の markdown を組み立て
  → IBlogPublisher で出力(dry-run がデフォルト)
```

### 動作仕様

| パラメータ | 値 | 意味 |
|---|---|---|
| 出力形式 | `{"title","slug","description","kind","tags","body"}` の JSONのみ | ツイートと同じ「JSONのみ出力」方式 |
| title | 40字以内・フィルタ通過 | |
| slug | `^[a-z0-9-]{3,50}$`。不正なら日付ベースにフォールバック | ファイル名・URL に使う |
| body | 200〜2000字・フィルタ通過 | 短すぎ/長すぎは再生成 |
| 重複 | 直近記事とタイトル完全一致なら再生成 | |
| 再生成 | 最大3回。全部失敗したらスキップ | TwitterBot と同じ |
| BLOG_DRY_RUN | デフォルト `1`(dry-run) | `0` で実際に commit & push |
| RECENT_POSTS_KEEP | 10 | 重複回避のため記憶する直近記事タイトル数 |
| 実行 | `--once` で1回生成して終了(当面は手動/タスクスケジューラ運用) | 常駐スケジュールは将来 |

### 公開方法 (IBlogPublisher)

```csharp
public interface IBlogPublisher
{
    Task PublishAsync(BlogPost post, CancellationToken ct = default);
}
```

- `DryRunBlogPublisher` — 生成された markdown をコンソール表示するだけ(デフォルト)
- `GitBlogPublisher` — ローカルクローン(`BLOG_REPO_PATH`)に対して
  `git pull → content/posts/ にファイル書き込み → git add/commit/push` を `git` CLI で実行。
  push の認証はユーザーの git 資格情報(credential manager)をそのまま使う。トークンをコードで扱わない
- 同名ファイルが既に存在する場合は上書きせず連番(`-2`)を付ける

### 環境変数

| 変数 | デフォルト | 意味 |
|---|---|---|
| `BLOG_DRY_RUN` | `1` | `0` で実 push |
| `BLOG_REPO_PATH` | (必須・実publish時) | ai-tuber-blogs のローカルクローンパス |

### プロンプト

`prompts/blog_system.md` を新規追加(人格は従来どおり `character.md` を結合)。
指示内容: 生活感の出し方は tweet_system.md と同方針、本文は 400〜800字目安で
「今日あったこと」を1テーマ掘り下げる、見出し(##)は0〜2個、実在の固有名詞はぼかす、
直近ツイート・配信メモと矛盾させない、JSONのみ出力。

### memory.json への追加

`MemoryData` に `RecentPosts`(直近記事のタイトル+日付、最大10件)を追加。
既存フィールドは変更しない(後方互換)。
