using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Medoz.X;
using DotNetEnv;

namespace PostX;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            // .envファイルを読み込む
            Env.Load();

            // 設定を構築
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            // OAuth 1.0a認証情報を取得
            var consumerKey = configuration["X_CONSUMER_KEY"];
            var consumerSecret = configuration["X_CONSUMER_SECRET"];
            var accessToken = configuration["X_ACCESS_TOKEN"];
            var accessTokenSecret = configuration["X_ACCESS_TOKEN_SECRET"];

            var clientId = configuration["X_CLIENT_ID"];
            var clientSecret = configuration["X_CLIENT_SECRET"];

            // 認証情報が設定されているか確認
            if (string.IsNullOrEmpty(consumerKey) || string.IsNullOrEmpty(consumerSecret) ||
                string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(accessTokenSecret))
            {
                Console.WriteLine("エラー: OAuth 1.0a認証情報が設定されていません。");
                Console.WriteLine(".envファイルに以下の変数を設定してください:");
                Console.WriteLine("X_CONSUMER_KEY, X_CONSUMER_SECRET, X_ACCESS_TOKEN, X_ACCESS_TOKEN_SECRET");
                return;
            }

            // XClientをOAuth 1.0a認証情報で初期化
            // var xClient = new XClient(consumerKey, consumerSecret, accessToken, accessTokenSecret);
            var xClient = new XClient(clientId!, clientSecret!);
            await xClient.AuthzAsync((url) =>
            {
                Console.WriteLine("以下のURLにアクセスして認証を行ってください:");
                Console.WriteLine(url);
            });

            // ユーザーにメッセージの入力を促す
            Console.WriteLine("Xに投稿するメッセージを入力してください (280文字以内):");
            var message = Console.ReadLine();

            if (string.IsNullOrEmpty(message))
            {
                Console.WriteLine("メッセージが入力されていません。プログラムを終了します。");
                return;
            }

            Console.WriteLine("投稿中...");

            // メッセージを投稿
            var response = await xClient.PostTweetAsync(message);

            // 結果を表示
            if (response.IsSuccess)
            {
                Console.WriteLine("投稿に成功しました！");
                Console.WriteLine($"ステータスコード: {response.StatusCode}");
                Console.WriteLine($"レスポンス: {response.Content}");
            }
            else
            {
                Console.WriteLine("投稿に失敗しました。");
                Console.WriteLine($"ステータスコード: {response.StatusCode}");
                Console.WriteLine($"エラー: {response.Content}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラーが発生しました: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"内部エラー: {ex.InnerException.Message}");
            }
        }
    }
}
