namespace Medoz.Voicevox.Tests;

public class AudioPlayerTests
{
    [Fact]
    public void FindDeviceIndex_部分一致で見つかる()
    {
        var names = new[] { "スピーカー (Realtek Audio)", "CABLE Input (VB-Audio Virtual Cable)" };
        Assert.Equal(1, AudioPlayer.FindDeviceIndex(names, "CABLE Input"));
    }

    [Fact]
    public void FindDeviceIndex_大文字小文字を無視する()
    {
        var names = new[] { "CABLE Input (VB-Audio Virtual Cable)" };
        Assert.Equal(0, AudioPlayer.FindDeviceIndex(names, "cable input"));
    }

    [Fact]
    public void FindDeviceIndex_見つからない場合はマイナス1()
    {
        var names = new[] { "スピーカー (Realtek Audio)" };
        Assert.Equal(-1, AudioPlayer.FindDeviceIndex(names, "CABLE Input"));
    }

    [Fact]
    public void FindDeviceIndex_複数一致は最初のデバイスを返す()
    {
        var names = new[] { "CABLE Input A", "CABLE Input B" };
        Assert.Equal(0, AudioPlayer.FindDeviceIndex(names, "CABLE Input"));
    }
}
