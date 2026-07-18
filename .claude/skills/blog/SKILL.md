---
name: blog
description: ブログ記事の壁打ち・執筆・公開。ペルソナの人格で記事を書き、ユーザーが承認したものだけ ai-tuber-blogs に commit & push して GitHub Pages で公開する。「ブログ書いて」「記事にして」「ブログに投稿して」でも適用。
---

# ブログ執筆スキル

Claude Code を編集者兼代筆者にして、キャラクターのブログ記事を対話で作り、公開する。
BlogBot(全自動生成)の Claude Code 版で、同じ契約・同じ原則に従う:

- **記事の真実は ai-tuber-blogs の `content/posts/*.md`**。フォーマット契約は
  docs/blog-architecture.md(BlogBot と共通。破壊的変更禁止)
- **push は外部公開**。ドラフトはまず全文を見せ、ユーザーが明示的に承認したものだけ
  commit & push する(dry-run 原則)
- 記事の人格・文体はペルソナの `character.md` + `blog_system.md` に従う。スキル側で口調を作らない

## 対象の解決

1. **ペルソナ**: 環境変数 `PERSONA_DIR`(PowerShell: `$env:PERSONA_DIR`)。未設定なら `personas/default`。
   本番人格「ぽとふ」は sibling の `../ai-tuber-potofu`。ユーザーが「ぽとふ」と言ったら
   PERSONA_DIR 未設定でもそちらを対象にする(どちらを対象にしたか必ず宣言する)。
   読むもの: `persona.json`(name / slug / bannedWords)、`character.md`、`blog_system.md`
   (blog_system.md が無いペルソナならその旨を伝えて中断)
2. **ブログリポジトリ**: 環境変数 `BLOG_REPO_PATH`。未設定なら sibling の `../ai-tuber-blogs`。
   `content/posts/` が無ければ場所をユーザーに確認する
3. **コンテキスト**(あれば読む。話題の種と重複回避に使う):
   - `data/<slug>/memory.json` — `stream_notes`(直近の配信メモ)/ `recent_tweets` / `recent_posts`
   - `content/posts/` の既存記事タイトル(直近10件程度。同テーマ・同タイトルの繰り返しを避ける)

## 使い方(引数)

- `/blog` — 話題探しから。配信メモ・直近ツイートから記事の種を2〜3個提案して選んでもらう
- `/blog <テーマ>` — そのテーマ(例: 「昨日の配信のふりかえり」「新しいゲームの話」)で書き始める
- `/blog お知らせ <内容>` — ユーザーが伝えたい内容を `kind: announce` の記事としてキャラの口調で代筆する

## 記事の作り方(壁打ち)

- 書くのは**キャラ本人の一人称ブログ**。`blog_system.md` の指示(1テーマ掘り下げ・400〜800字目安・
  見出し `##` 0〜2個・実在固有名詞を出さない・感情タグ/URL/@/ハッシュタグ禁止)に従う
- 流れ: テーマと切り口を短く確認 → **本文ドラフトを全文提示** → ユーザーの添削を反映。
  ドラフト前に質問を重ねすぎない(1〜2往復で一度書いてしまい、直しながら詰める)
- 配信メモ・直近ツイート・既存記事と矛盾する内容を書かない。書いた出来事の日付は今日基準で自然に
- ユーザーが本文を持ち込んだ場合は、キャラの文体に整えることを提案しつつ原文の意図を変えない

## 検証(公開前チェックリスト。BlogBot と同じ契約)

| 項目 | ルール |
|---|---|
| ファイル名 | `content/posts/YYYY-MM-DD-<slug>.md`(日付は今日) |
| `title` | 40字以内 |
| `slug` | `^[a-z0-9-]{3,50}$` |
| `description` | 80字程度。キャラの口調のまま |
| `date` | 今日時 ISO 8601 `+09:00`(例: `2026-07-18T21:00:00+09:00`) |
| `kind` | `daily` / `stream` / `game` / `announce` のいずれか |
| `tags` | 日本語の短い単語 1〜3個 |
| 本文 | 200〜2000字(目安 400〜800字)。見出しは `##` から |
| 禁止ワード | エンジン共通セット(`AppConfig.BannedWords`: 死ね・殺す・`http://`・`@`)+ persona.json の `bannedWords` を含まない |
| 重複 | `recent_posts` および既存記事とタイトル完全一致しない |

1つでも満たさなければ公開せず、直してから再提示する。

## 公開(承認後のみ)

1. frontmatter 込みの**完成形 markdown 全文**とファイル名を見せ、「この内容で公開してよいか」を確認する。
   承認されるまで push しない(「いい感じにして」「任せる」だけでは公開しない)
2. 承認後、ブログリポジトリで GitBlogPublisher と同じ手順を実行:
   `git pull --ff-only` → `content/posts/` にファイル書き込み(同名が既にあれば上書きせず `-2` 連番)→
   `git add` → `git commit -m "post: <title>"` → `git push`
3. push 後、公開URL(`https://atoyr.github.io/ai-tuber-blogs/`。GitHub Actions のビルドで数分後に反映)を伝える
4. `data/<slug>/memory.json` の `recent_posts` 先頭に `{"date": "yyyy-MM-dd HH:mm", "title": "<title>"}` を
   追加する(最大10件、超えた分は末尾から削除。SharedMemory と同じ形式)。
   これで BlogBot(自動生成)側が同じテーマ・タイトルを繰り返さなくなる

## やらないこと

- 承認前の push・ai-tuber-blogs の記事以外(デザイン・設定・過去記事)の変更
- `character.md` や `blog_system.md` の書き換え(人格を直したくなったら `/persona` へ)
- 過去記事の削除・改稿(頼まれた場合も「公開済み記事の変更」であることを確認してから)
