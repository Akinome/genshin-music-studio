using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class GameInputGuardTests
{
    private static InstrumentUiSignature Signature() =>
        InstrumentUiSignature.Capture(new[] { (0, 0) }, new[] { new ObservedColor(10, 10, 10) });

    [Fact]
    public void Check_IsDisabledUntilASignatureIsArmed()
    {
        var guard = new GameInputGuard();

        Assert.False(guard.Enabled);
        Assert.Equal(GuardDecision.Disabled, guard.Check(0));
    }

    [Fact]
    public void Arm_EnablesTheGuardOnlyWithUsablePoints()
    {
        var guard = new GameInputGuard();

        guard.Arm(Signature());
        Assert.True(guard.Enabled);

        guard.Arm(null);
        Assert.False(guard.Enabled);
        Assert.Null(guard.Signature);

        guard.Arm(new InstrumentUiSignature(Array.Empty<ProbePoint>(), 10));
        Assert.False(guard.Enabled);
    }

    [Fact]
    public void Check_KeepsPlayingWhileTheInterfaceStillMatches()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(1.0));
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.5));
        Assert.Equal(0, guard.ConsecutiveMisses);
    }

    [Fact]
    public void Check_PausesOnlyAfterConsecutiveMisses()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        // 0.3 高于硬阈值 0.25，只算普通未命中，攒满两次才暂停。
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.3));
        Assert.Equal(1, guard.ConsecutiveMisses);

        Assert.Equal(GuardDecision.Pause, guard.Check(0.3));
        // 暂停后重新开始计数，避免恢复演奏时又被同类判定立刻打断。
        Assert.Equal(0, guard.ConsecutiveMisses);
    }

    [Fact]
    public void Check_PausesImmediatelyWhenAlmostNothingMatches()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        // 界面确实没了，第一次取样就暂停，不再攒连续未命中。
        Assert.Equal(GuardDecision.Pause, guard.Check(0.25));
        Assert.Equal(0, guard.ConsecutiveMisses);
        Assert.Equal(GuardDecision.Pause, guard.Check(0.0));
    }

    [Fact]
    public void Check_DoesNotPauseOnASingleMissBetweenTheTwoTiers()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        // 0.25 到 0.5 之间只算一次普通未命中，不会立刻暂停。
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.4));
        Assert.Equal(1, guard.ConsecutiveMisses);

        // 界面恢复后计数清零，下一次未命中重新开始攒。
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.9));
        Assert.Equal(0, guard.ConsecutiveMisses);
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.4));
        Assert.Equal(1, guard.ConsecutiveMisses);
    }

    [Fact]
    public void Check_ResetsTheStreakWhenTheInterfaceComesBack()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.3));
        Assert.Equal(1, guard.ConsecutiveMisses);

        // 界面回来了，计数清零。
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.9));
        Assert.Equal(0, guard.ConsecutiveMisses);

        // 界面再次消失后重新攒，不会因为清零而被立刻误判。
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.3));
        Assert.Equal(1, guard.ConsecutiveMisses);
        Assert.Equal(GuardDecision.Pause, guard.Check(0.3));
        Assert.Equal(0, guard.ConsecutiveMisses);
    }

    [Fact]
    public void Reset_ClearsTheMissStreak()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        guard.Check(0.3);
        guard.Check(0.3);
        guard.Reset();

        Assert.Equal(0, guard.ConsecutiveMisses);
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.3));
    }

    [Fact]
    public void Disarm_StopsJudging()
    {
        var guard = new GameInputGuard();
        guard.Arm(Signature());

        guard.Disarm();

        Assert.False(guard.Enabled);
        Assert.Equal(GuardDecision.Disabled, guard.Check(0));
    }

    [Fact]
    public void Thresholds_CanBeTightenedForSensitiveSetups()
    {
        var guard = new GameInputGuard(missThreshold: 1, matchThreshold: 0.9);
        guard.Arm(Signature());

        Assert.Equal(1, guard.MissThreshold);
        Assert.Equal(0.9, guard.MatchThreshold);
        Assert.Equal(GameInputGuard.DefaultHardMatchThreshold, guard.HardMatchThreshold);
        Assert.Equal(GuardDecision.KeepPlaying, guard.Check(0.9));
        Assert.Equal(GuardDecision.Pause, guard.Check(0.89));
    }

    [Fact]
    public void Constructor_ClampsOutOfRangeThresholds()
    {
        Assert.Equal(1, new GameInputGuard(0).MissThreshold);
        Assert.Equal(GameInputGuard.DefaultMatchThreshold, new GameInputGuard(matchThreshold: 4.0).MatchThreshold);
        Assert.Equal(GameInputGuard.DefaultMatchThreshold, new GameInputGuard(matchThreshold: -1.0).MatchThreshold);
        Assert.Equal(GameInputGuard.DefaultHardMatchThreshold, new GameInputGuard(hardMatchThreshold: 2.0).HardMatchThreshold);
        Assert.Equal(GameInputGuard.DefaultHardMatchThreshold, new GameInputGuard(hardMatchThreshold: -0.5).HardMatchThreshold);
    }
}
