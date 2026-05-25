using System;
using Dalamud.Plugin.Services;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Plugin.Dalamud.GameState;

public sealed class StateAggregator : IDisposable
{
    private readonly AddonEmjReader reader;
    private readonly IFramework framework;
    private readonly IPolicy? policy;
    private bool disposed;
    private long lastRebuildTicks;
    private int lastContentHash;
    private bool hasContentHash;
    private const long MinTickIntervalTicks = 160_000;

    public StateSnapshot? Latest { get; private set; }

    /// <summary>Scored discards for <see cref="Latest"/>; null off our turn or on scorer throw.</summary>
    public ScoredDiscard[]? LastScored { get; private set; }

    /// <summary>Policy verdict for <see cref="Latest"/>; null when Legal=None or on policy throw.</summary>
    public ActionChoice? LastChoice { get; private set; }

    /// <summary>Scorer exception message, paired with <see cref="LastScored"/>=null.</summary>
    public string? LastScorerError { get; private set; }

    public event Action<StateSnapshot>? Changed;

    public StateAggregator(AddonEmjReader reader, IFramework framework, IPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(framework);
        this.reader = reader;
        this.framework = framework;
        this.policy = policy;

        this.reader.ObservationChanged += OnObservationChanged;
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        framework.Update -= OnFrameworkUpdate;
        reader.ObservationChanged -= OnObservationChanged;
    }

    private void OnObservationChanged(AddonEmjObservation _) => Rebuild();

    private void OnFrameworkUpdate(IFramework _)
    {
        long now = DateTime.UtcNow.Ticks;
        if (now - lastRebuildTicks < MinTickIntervalTicks)
            return;
        lastRebuildTicks = now;
        Rebuild();
    }

    private void Rebuild()
    {
        // Always call TryBuildSnapshot: it observes MeldTracker and pins ActiveLayout.
        var next = reader.TryBuildSnapshot();
        if (next is null)
        {
            // Addon gone (player left the table) — drop cached state so the UI reverts to the "waiting" empty state.
            if (Latest is not null)
            {
                Latest = null;
                LastScored = null;
                LastChoice = null;
                LastScorerError = null;
                hasContentHash = false;
            }
            return;
        }
        if (next.SchemaVersion != StateSnapshot.CurrentSchemaVersion)
            return;

        int hash = ComputeContentHash(next);
        if (hasContentHash && hash == lastContentHash)
            return;

        lastContentHash = hash;
        hasContentHash = true;
        Latest = next;
        RefreshPolicyCache(next);
        Changed?.Invoke(next);
    }

    private void RefreshPolicyCache(StateSnapshot snap)
    {
        LastScored = null;
        LastChoice = null;
        LastScorerError = null;

        if (policy is null)
            return;
        if (snap.Legal.Flags == ActionFlags.None)
            return;

        if (snap.Legal.Can(ActionFlags.Discard))
        {
            try
            { LastScored = DiscardScorer.Score(snap); }
            catch (Exception ex)
            { LastScorerError = ex.Message; }
        }

        try
        { LastChoice = policy.Choose(snap); }
        catch { }
    }

    /// <summary>Content fingerprint; record equality reference-checks list fields and reports false on every fresh snapshot.</summary>
    private static int ComputeContentHash(StateSnapshot snap)
    {
        var h = new HashCode();
        h.Add(snap.WallRemaining);
        h.Add(snap.TurnIndex);
        h.Add((int)snap.Legal.Flags);
        h.Add(snap.Legal.PonCandidates.Count);
        h.Add(snap.Legal.ChiCandidates.Count);
        h.Add(snap.Legal.KanCandidates.Count);
        h.Add(snap.OurRiichi);
        h.Add(snap.OurIppatsu);
        h.Add(snap.OurSeat);
        h.Add(snap.RoundWind);
        h.Add(snap.DealerSeat);
        h.Add(snap.Honba);
        h.Add(snap.RiichiSticks);
        h.Add(snap.AkaDora);
        h.Add(snap.AddonStateCode);
        foreach (var t in snap.Hand)
            h.Add(t.Id);
        foreach (var m in snap.OurMelds)
        {
            h.Add((int)m.Kind);
            foreach (var t in m.Tiles)
                h.Add(t.Id);
        }
        foreach (var t in snap.DoraIndicators)
            h.Add(t.Id);
        foreach (var s in snap.Scores)
            h.Add(s);
        foreach (var s in snap.Seats)
        {
            h.Add(s.DiscardCount);
            foreach (var t in s.Discards)
                h.Add(t.Id);
            foreach (var m in s.Melds)
            {
                h.Add((int)m.Kind);
                foreach (var t in m.Tiles)
                    h.Add(t.Id);
            }
            h.Add(s.Riichi);
            h.Add(s.RiichiDiscardIndex);
            h.Add(s.Ippatsu);
        }
        return h.ToHashCode();
    }
}
