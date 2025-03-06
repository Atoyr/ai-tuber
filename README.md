# AI Tuber
このリポジトリは AI Tuberを動かすためのツール一式です。

# 機能
[X] LLMからメッセージの送受信
[X] Xへの投稿機能
[ ] ブログへの投稿機能
[ ] VOIVRVOXを使ったおしゃべり機能

## PostX - Xへの投稿機能

PostXは、コンソールから入力したテキストをXに投稿するためのコマンドラインツールです。

### セットアップ

1. `.env.sample`ファイルを`.env`にコピーします。
   ```
   cp PostX/.env.sample PostX/.env
   ```

2. `.env`ファイルを編集して、X API認証情報を設定します。
   ```
   X_CONSUMER_KEY=your_consumer_key
   X_CONSUMER_SECRET=your_consumer_secret
   X_ACCESS_TOKEN=your_access_token
   X_ACCESS_TOKEN_SECRET=your_access_token_secret
   ```

   X API認証情報は、[X Developer Portal](https://developer.twitter.com/en/portal/dashboard)から取得できます。

### 使用方法

1. プロジェクトをビルドします。
   ```
   dotnet build
   ```

2. PostXを実行します。
   ```
   dotnet run --project PostX
   ```

3. プロンプトが表示されたら、Xに投稿するメッセージを入力します。
   ```
   Xに投稿するメッセージを入力してください (280文字以内):
   ```

4. 投稿が成功すると、成功メッセージとレスポンスが表示されます。