using System.Threading.Channels;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Core.Missions;

public enum BuyingAgentOutcome
{
    /// <summary>A rolled mission list matched the watch.</summary>
    Matched,

    /// <summary>The maximum number of rolls was reached with no match.</summary>
    Exhausted,

    /// <summary>The user stopped it.</summary>
    Stopped,

    /// <summary>The hook could not roll (nothing recorded, no window, not supported).</summary>
    Failed,
}

public sealed record BuyingAgentResult(BuyingAgentOutcome Outcome, int Rolls, MissionList? Match, string Message);

/// <summary>
/// Rolls missions through the hook until one matches the watch or a limit is reached, without
/// touching the mouse: each roll repeats the player's own last Request, and the resulting mission
/// list arrives through the normal message pipeline. The retail terminal enforces a minimum time
/// between rolls, which <see cref="minInterval"/> respects.
/// </summary>
public sealed class BuyingAgent
{
    private readonly Func<CancellationToken, Task<CommandStatus>> roll;
    private readonly Func<MissionList, bool> isMatch;
    private readonly TimeSpan minInterval;
    private readonly TimeSpan listTimeout;
    private readonly Channel<MissionList> lists = Channel.CreateUnbounded<MissionList>(new UnboundedChannelOptions { SingleReader = true });

    public BuyingAgent(
        Func<CancellationToken, Task<CommandStatus>> roll,
        Func<MissionList, bool> isMatch,
        TimeSpan? minInterval = null,
        TimeSpan? listTimeout = null)
    {
        this.roll = roll;
        this.isMatch = isMatch;
        this.minInterval = minInterval ?? TimeSpan.FromMilliseconds(2100);
        this.listTimeout = listTimeout ?? TimeSpan.FromSeconds(6);
    }

    /// <summary>Feed a mission list the client produced. Call this for every list while running.</summary>
    public void Submit(MissionList list) => this.lists.Writer.TryWrite(list);

    public async Task<BuyingAgentResult> RunAsync(int maxRolls, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        for (int roll = 1; roll <= maxRolls; roll++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(roll);

            Drain();
            CommandStatus status = await this.roll(cancellation).ConfigureAwait(false);
            switch (status)
            {
                case CommandStatus.Done:
                    break;
                case CommandStatus.NothingRecorded:
                    return new BuyingAgentResult(BuyingAgentOutcome.Failed, roll - 1, null, "Roll the terminal once by hand first, so the agent can repeat it.");
                case CommandStatus.NotSupported:
                    return new BuyingAgentResult(BuyingAgentOutcome.Failed, roll - 1, null, "This client cannot roll missions from the hook.");
                case CommandStatus.WindowNotFound:
                    return new BuyingAgentResult(BuyingAgentOutcome.Failed, roll - 1, null, "The game window was not found.");
                case CommandStatus.Busy:
                    await this.WaitBetweenRolls(cancellation).ConfigureAwait(false);
                    continue;
                default:
                    return new BuyingAgentResult(BuyingAgentOutcome.Failed, roll - 1, null, $"The hook rejected the roll ({status}).");
            }

            MissionList? list = await this.WaitForListAsync(cancellation).ConfigureAwait(false);
            if (list is not null && this.isMatch(list))
            {
                return new BuyingAgentResult(BuyingAgentOutcome.Matched, roll, list, $"Matched on roll {roll}.");
            }

            if (roll < maxRolls)
            {
                await this.WaitBetweenRolls(cancellation).ConfigureAwait(false);
            }
        }

        return new BuyingAgentResult(BuyingAgentOutcome.Exhausted, maxRolls, null, $"No match after {maxRolls} rolls.");
    }

    private void Drain()
    {
        while (this.lists.Reader.TryRead(out _))
        {
        }
    }

    private async Task<MissionList?> WaitForListAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(this.listTimeout);
        try
        {
            return await this.lists.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // The roll produced no list in time; try again.
            return null;
        }
    }

    private Task WaitBetweenRolls(CancellationToken cancellation) => Task.Delay(this.minInterval, cancellation);
}
