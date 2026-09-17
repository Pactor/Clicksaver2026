using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

public sealed class BuyingAgentTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(5);

    private static MissionList ListWithQuality(int quality)
    {
        var mission = new Mission(1, "s", "d", 100, 10, [new MissionRewardItem(1, 1, quality)], quality, 0x2C49, 687, 1, 2);
        return new MissionList(6, [0, 0, 0, 0, 0, 0], 55, [mission]);
    }

    [Fact]
    public async Task StopsOnTheFirstMatchingRoll()
    {
        int[] qualities = [30, 40, 55, 70];
        int roll = 0;
        BuyingAgent agent = null!;
        agent = new BuyingAgent(
            _ =>
            {
                agent.Submit(ListWithQuality(qualities[roll++]));
                return Task.FromResult(CommandStatus.Done);
            },
            list => list.Missions[0].Quality >= 50,
            Fast,
            TimeSpan.FromSeconds(2));

        BuyingAgentResult result = await agent.RunAsync(10, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(BuyingAgentOutcome.Matched, result.Outcome);
        Assert.Equal(3, result.Rolls); // 30, 40, then 55 matches
        Assert.Equal(55, result.Match!.Missions[0].Quality);
    }

    [Fact]
    public async Task StopsAfterTheRollLimitWithNoMatch()
    {
        BuyingAgent agent = null!;
        agent = new BuyingAgent(
            _ =>
            {
                agent.Submit(ListWithQuality(10));
                return Task.FromResult(CommandStatus.Done);
            },
            _ => false,
            Fast,
            TimeSpan.FromSeconds(2));

        BuyingAgentResult result = await agent.RunAsync(4, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(BuyingAgentOutcome.Exhausted, result.Outcome);
        Assert.Equal(4, result.Rolls);
    }

    [Fact]
    public async Task ReportsWhenNothingWasRecordedToRepeat()
    {
        var agent = new BuyingAgent(_ => Task.FromResult(CommandStatus.NothingRecorded), _ => true, Fast);

        BuyingAgentResult result = await agent.RunAsync(5, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(BuyingAgentOutcome.Failed, result.Outcome);
        Assert.Contains("by hand", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CountsRollsThroughProgress()
    {
        var seen = new List<int>();
        BuyingAgent agent = null!;
        agent = new BuyingAgent(
            _ =>
            {
                agent.Submit(ListWithQuality(10));
                return Task.FromResult(CommandStatus.Done);
            },
            _ => false,
            Fast);

        await agent.RunAsync(3, new Progress<int>(seen.Add), TestContext.Current.CancellationToken);

        // Progress reports each roll number in order (there may be a small delay before the last is observed).
        Assert.Equal([1, 2, 3], seen.Distinct().Order());
    }

    [Fact]
    public async Task CanBeStopped()
    {
        using var cts = new CancellationTokenSource();
        var agent = new BuyingAgent(
            async ct =>
            {
                await cts.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return CommandStatus.Done;
            },
            _ => false,
            Fast);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.RunAsync(5, cancellation: cts.Token));
    }
}
