namespace Medoz.TwitterBot;

/// <summary>
/// 投稿タイミングの判定 (Python版 twitter_bot.py の in_active_hours / 待機間隔 相当)。
/// </summary>
public static class TweetScheduler
{
    /// <summary>指定時刻が投稿可能な時間帯 (startHour 以上 endHour 未満) なら true</summary>
    public static bool InActiveHours(int hour, int startHour, int endHour)
        => startHour <= hour && hour < endHour;

    /// <summary>指定日時が投稿可能な時間帯なら true</summary>
    public static bool InActiveHours(DateTime now, int startHour, int endHour)
        => InActiveHours(now.Hour, startHour, endHour);

    /// <summary>
    /// 次の投稿までの待機分数を [minMinutes, maxMinutes] からランダムに選ぶ。
    /// Python の random.randint は両端を含むため maxMinutes まで含める。
    /// </summary>
    public static int NextIntervalMinutes(Random random, int minMinutes, int maxMinutes)
        => random.Next(minMinutes, maxMinutes + 1);
}
