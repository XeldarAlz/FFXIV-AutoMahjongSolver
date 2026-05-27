using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Mahjong.Plugin.Dalamud.GameState.Variants;

internal sealed class BaseEmjVariant : IEmjVariant
{
    // Diagnostic dumps go to +0x3000; production reads max out at DoraIndicator (+0x0FD8). Single budget keeps the runtime/fixture shapes aligned.
    private const int AddonMemorySize = 0x3000;

    private readonly LayoutProfile profile;
    private readonly IPluginLog log;
    private readonly string pluginConfigDir;

    // Recomputed per snapshot from the hand array; tracks the configured base unless a client tile-ID shift forces a retune (issue #52).
    private int effectiveTextureBase;
    private int lastWarnedTextureBase;

    public BaseEmjVariant(LayoutProfile profile, IPluginLog log, string pluginConfigDir)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDir);
        this.profile = profile;
        this.log = log;
        this.pluginConfigDir = pluginConfigDir;
        effectiveTextureBase = profile.TileTextureBase;
        lastWarnedTextureBase = profile.TileTextureBase;
    }

    public string Name => profile.Name;
    public string PreferredAddonName => profile.AddonName;
    public LayoutProfile Profile => profile;

    private int lastLoggedCallPromptState = -1;
    private int lastLoggedMeldHandCount = -1;

    public unsafe bool Probe(AtkUnitBase* unit)
    {
        if (unit == null || unit->RootNode == null)
            return false;

        int selfScore = *(int*)((byte*)unit + profile.Offsets.SelfScore);
        if (selfScore < 0 || selfScore > profile.Limits.ScoreSanityMax)
            return false;

        if (unit->GetNodeById(profile.NodeIds.CallModalHost) == null)
            return false;

        byte* basePtr = (byte*)unit;
        int len = profile.Limits.HandSize;
        Span<int> raw = stackalloc int[len];
        for (int i = 0; i < len; i++)
            raw[i] = *(int*)(basePtr + profile.Offsets.HandArrayStart + i * 4);
        int probeBase = HandArrayDecoder.ResolveTextureBase(raw, profile.TileTextureBase, out _);
        int valid = 0;
        int unknown = 0;
        for (int i = 0; i < len; i++)
        {
            if (raw[i] == 0)
                break;
            int tileId = HandArrayDecoder.DecodeTileId(raw[i], probeBase, out _);
            if (tileId >= 0)
                valid++;
            else
                unknown++;
        }
        // Empty hand: no tile evidence — fall through to the name tiebreaker.
        if (valid == 0 && unknown == 0)
            return true;
        return valid >= 1 && unknown <= profile.Limits.MaxAkadoraSlots;
    }

    public unsafe StateSnapshot? TryBuildSnapshot(AtkUnitBase* unit, VariantReadContext ctx)
    {
        if (unit == null)
            return null;
        var memory = new ReadOnlySpan<byte>((void*)unit, AddonMemorySize);
        var atkValues = SnapshotAtkValues(unit->AtkValues, unit->AtkValuesCount);
        bool modalVisible = IsCallModalVisible(unit);
        var listLabels = modalVisible ? ReadVisibleListItemLabels(unit) : null;
        return BuildSnapshotFromMemory(
            memory, atkValues, ctx,
            callModalVisible: modalVisible,
            listWidgetLabels: listLabels,
            enableDiagnosticLogging: true);
    }

    /// <summary>Pure entry — no Dalamud pointers. Fixture replay drives this directly with a captured byte span and decoded AtkValues.</summary>
    public StateSnapshot? BuildSnapshotFromMemory(
        ReadOnlySpan<byte> memory,
        IReadOnlyList<AtkValueRecord> atkValues,
        VariantReadContext ctx,
        bool callModalVisible,
        IReadOnlyList<string>? listWidgetLabels = null,
        bool enableDiagnosticLogging = false)
    {
        int akaDora = 0;
        var hand = ReadHand(memory, ref akaDora);

        var scores = ReadScores(memory);
        if (!ScoresPlausible(scores))
            return null;

        var doraIndicators = ReadDoraIndicators(memory);
        var discardCounts = ReadDiscardCounts(memory);

        int stateCode = ReadStateCode(atkValues);
        int wallRemaining = ResolveWallRemaining(discardCounts);

        var seats = BuildSeatViews(memory, discardCounts);
        var legal = BuildLegalActions(stateCode, hand, atkValues, callModalVisible, listWidgetLabels);

        if (enableDiagnosticLogging)
        {
            MaybeLogCallPromptTransition(ctx, memory, stateCode, atkValues, hand, legal);
            MaybeLogMeldTransition(ctx, memory, stateCode, hand);
        }

        ctx.MeldTracker.ObserveWall(wallRemaining);
        ctx.MeldTracker.ObserveSnapshot(hand, discardCounts, ourSeat: 0, currentAkadora: akaDora);
        var ourMelds = ctx.MeldTracker.Melds.ToArray();
        int totalAkadora = akaDora + ctx.MeldTracker.MeldAkadora;

        return StateSnapshot.Empty with
        {
            Hand = hand,
            OurMelds = ourMelds,
            Scores = scores,
            Seats = seats,
            WallRemaining = wallRemaining,
            DoraIndicators = doraIndicators,
            Legal = legal,
            // Solo Doman is tonpuusen — pin to East-seat East-round; seat wind ends up wrong for hands 2-4 but round wind stays correct.
            OurSeat = 0,
            RoundWind = 0,
            SeatInfoKnown = true,
            AkaDora = totalAkadora,
            AddonStateCode = stateCode,
        };
    }

    private static int ReadInt32(ReadOnlySpan<byte> memory, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(memory.Slice(offset, 4));

    private List<Tile> ReadHand(ReadOnlySpan<byte> memory, ref int akaDora)
    {
        int len = profile.Limits.HandSize;
        Span<int> raw = stackalloc int[len];
        for (int i = 0; i < len; i++)
            raw[i] = ReadInt32(memory, profile.Offsets.HandArrayStart + i * 4);
        // Drives every decode this snapshot (hand, dora, discards, atk-value tiles) off one resolved base.
        effectiveTextureBase = HandArrayDecoder.ResolveTextureBase(raw, profile.TileTextureBase, out bool shifted);
        if (shifted)
            WarnTextureBaseShift();
        var (tiles, aka) = HandArrayDecoder.ReadHand(raw, effectiveTextureBase);
        akaDora += aka;
        return tiles;
    }

    private void WarnTextureBaseShift()
    {
        if (effectiveTextureBase == lastWarnedTextureBase)
            return;
        lastWarnedTextureBase = effectiveTextureBase;
        log.Warning(
            $"[{Name}] tile texture base shifted: configured {profile.TileTextureBase}, decoding with " +
            $"{effectiveTextureBase} (delta {effectiveTextureBase - profile.TileTextureBase}). " +
            "A client patch likely moved tile texture IDs; update the layout profile.");
    }

    private int DecodeTileId(int raw) =>
        HandArrayDecoder.DecodeTileId(raw, effectiveTextureBase, out _);

    private int[] ReadScores(ReadOnlySpan<byte> memory) =>
    [
        ReadInt32(memory, profile.Offsets.SelfScore),
        ReadInt32(memory, profile.Offsets.ShimochaScore),
        ReadInt32(memory, profile.Offsets.ToimenScore),
        ReadInt32(memory, profile.Offsets.KamichaScore),
    ];

    private bool ScoresPlausible(int[] scores)
    {
        int max = profile.Limits.ScoreSanityMax;
        foreach (var s in scores)
            if (s < 0 || s > max)
                return false;
        return true;
    }

    private List<Tile> ReadDoraIndicators(ReadOnlySpan<byte> memory)
    {
        var dora = new List<Tile>(1);
        int rawDora = ReadInt32(memory, profile.Offsets.DoraIndicator);
        int doraTileId = DecodeTileId(rawDora);
        if (doraTileId >= 0)
            dora.Add(Tile.FromId(doraTileId));
        return dora;
    }

    private int[] ReadDiscardCounts(ReadOnlySpan<byte> memory)
    {
        var counts = new int[4]
        {
            memory[profile.Offsets.SelfDiscardCountByte],
            memory[profile.Offsets.ShimochaDiscardCountByte],
            memory[profile.Offsets.ToimenDiscardCountByte],
            memory[profile.Offsets.KamichaDiscardCountByte],
        };
        int cap = profile.Limits.DiscardCountSanityMax;
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > cap)
                counts[i] = 0;
        return counts;
    }

    private int ReadStateCode(IReadOnlyList<AtkValueRecord> atkValues)
    {
        int idx = profile.AtkValues.StateCode;
        if (atkValues.Count <= idx)
            return -1;
        var v = atkValues[idx];
        return v.Type == ValueType.Int ? v.IntValue : -1;
    }

    // wall_remaining = initial_live_wall − total_discards. Ignores kan dead-wall draws (minor under-estimate). atkValues[WallCount] uses a different baseline and flips at state transitions, breaking hand-roll detection.
    private int ResolveWallRemaining(int[] discardCounts)
    {
        int totalDiscards = 0;
        foreach (var c in discardCounts)
            totalDiscards += c;
        int derived = profile.Limits.WallInitial - totalDiscards;
        return derived >= 0 && derived <= profile.Limits.WallInitial ? derived : profile.Limits.WallInitial;
    }

    private SeatView[] BuildSeatViews(ReadOnlySpan<byte> memory, int[] discardCounts)
    {
        var seats = new SeatView[4];
        int?[] discardArrayOffsets =
        [
            profile.Offsets.SelfDiscardArray,
            profile.Offsets.ShimochaDiscardArray,
            profile.Offsets.ToimenDiscardArray,
            profile.Offsets.KamichaDiscardArray,
        ];
        for (int i = 0; i < 4; i++)
        {
            var discards = ReadDiscardArray(memory, discardArrayOffsets[i], discardCounts[i]);
            seats[i] = new SeatView(
                Discards: discards,
                DiscardIsTedashi: Enumerable.Repeat(true, discards.Count).ToList(),
                Melds: [],
                Riichi: false,
                RiichiDiscardIndex: -1,
                Ippatsu: false,
                IsTenpaiCalled: false,
                DiscardCount: discardCounts[i]);
        }
        return seats;
    }

    /// <summary>Every discard is reported as tedashi — per-tile tedashi bits aren't mapped yet.</summary>
    private IReadOnlyList<Tile> ReadDiscardArray(ReadOnlySpan<byte> memory, int? offset, int reportedCount)
    {
        if (offset is not int off || reportedCount <= 0)
            return [];
        int len = Math.Min(reportedCount, profile.Offsets.DiscardArrayMaxLen);
        var tiles = new List<Tile>(len);
        for (int i = 0; i < len; i++)
        {
            int raw = ReadInt32(memory, off + i * 4);
            if (raw == 0)
                break;
            int tileId = DecodeTileId(raw);
            if (tileId < 0)
                continue;
            tiles.Add(Tile.FromId(tileId));
        }
        return tiles;
    }

    /// <summary>State-6 (SelfDeclareList) is dual-use: hand=14 is the self-declare popup, hand!=14 with %3==2 is the post-call discard-from-list popup — gate on hand=14 or stale "Pon" labels strand the loop.</summary>
    private LegalActions BuildLegalActions(
        int stateCode, List<Tile> hand, IReadOnlyList<AtkValueRecord> atkValues,
        bool callModalVisible, IReadOnlyList<string>? listWidgetLabels)
    {
        var states = profile.StateCodes;
        bool isCallPromptState =
            stateCode == states.CallPrompt ||
            stateCode == states.CallPromptList ||
            (stateCode == states.SelfDeclareList && hand.Count == 14);

        if (isCallPromptState && callModalVisible)
        {
            const ActionFlags acceptMask =
                ActionFlags.Pon | ActionFlags.Chi |
                ActionFlags.MinKan | ActionFlags.ShouMinKan |
                ActionFlags.Ron | ActionFlags.Riichi | ActionFlags.Tsumo;
            LegalActions scanned;
            if (atkValues.Count > 0)
            {
                scanned = BuildCallPromptLegal(hand, atkValues);
                if ((scanned.Flags & acceptMask) == 0)
                    scanned = BuildCallPromptLegalFromListItems(hand, atkValues, listWidgetLabels);
            }
            else
            {
                scanned = BuildCallPromptLegalFromListItems(hand, atkValues, listWidgetLabels);
            }

            // State-6 hand=14 popup also exposes the closed hand as a discard surface; surface Discard here so policy.Choose can pick a tile when Pass is the call verdict.
            bool isSelfDeclarePopup =
                stateCode == states.SelfDeclareList && hand.Count == 14;
            if (isSelfDeclarePopup)
            {
                scanned = scanned with { Flags = scanned.Flags | ActionFlags.Discard };
            }
            return scanned;
        }

        // Our turn = hand % 3 == 2 (14/11/8/5/2). Post-minkan-pre-rinshan (closed=10 with 1 minkan) is intentionally not discard-eligible.
        if (hand.Count > 0 && hand.Count % 3 == 2)
            return new LegalActions(ActionFlags.Discard, [], [], [], []);

        return LegalActions.None;
    }

    private unsafe AtkValueRecord[] SnapshotAtkValues(AtkValue* atkValues, ushort atkCount)
    {
        if (atkValues == null || atkCount == 0)
            return [];
        int n = atkCount;
        var result = new AtkValueRecord[n];
        for (int i = 0; i < n; i++)
        {
            var v = atkValues[i];
            string? s = null;
            if ((v.Type == ValueType.String || v.Type == ValueType.String8 || v.Type == ValueType.ManagedString)
                && v.String.Value != null)
            {
                s = v.String.ToString();
            }
            result[i] = new AtkValueRecord(
                Type: v.Type,
                IntValue: v.Type == ValueType.Int ? v.Int : 0,
                UIntValue: v.Type == ValueType.UInt ? v.UInt : (v.Type == ValueType.Bool ? (uint)(v.Byte != 0 ? 1 : 0) : 0u),
                StringValue: s);
        }
        return result;
    }

    private unsafe bool IsCallModalVisible(AtkUnitBase* unit)
    {
        if (unit == null)
            return false;
        var host = unit->GetNodeById(profile.NodeIds.CallModalHost);
        if (host == null)
            return false;
        // Component node types are >= 1000; type-check guards against a future patch renumbering host to a native node.
        if ((int)host->Type < 1000)
            return false;
        var comp = ((AtkComponentNode*)host)->Component;
        if (comp == null)
            return false;
        var shell = comp->GetNodeById(profile.NodeIds.CallModalShell);
        return shell != null && shell->NodeFlags.HasFlag(NodeFlags.Visible);
    }

    private LegalActions BuildCallPromptLegal(List<Tile> hand, IReadOnlyList<AtkValueRecord> atkValues)
    {
        var labels = ScanButtonLabels(atkValues, scanLimit: profile.AtkValues.ButtonLabelScanLimit);
        if (!labels.HasAnyAcceptOffer)
            return new LegalActions(ActionFlags.Pass, [], [], [], []);

        ActionFlags flags = ActionFlags.Pass;
        var pons = new List<MeldCandidate>();
        var chis = new List<MeldCandidate>();
        var kans = new List<MeldCandidate>();

        if (labels.OffersRon)
            flags |= ActionFlags.Ron;
        if (labels.OffersRiichi)
            flags |= ActionFlags.Riichi;
        if (labels.OffersTsumo)
            flags |= ActionFlags.Tsumo;

        var counts = new int[Tile.Count34];
        foreach (var t in hand)
            counts[t.Id]++;

        if (labels.OffersPon)
        {
            flags |= ActionFlags.Pon;
            // Doman exposes the discarded tile as a consecutive duplicate in [16..21]; fall back to unique-pair heuristic.
            AppendPonCandidateFromAtkValues(hand, counts, atkValues, pons);
            if (pons.Count == 0)
                AppendPonCandidate(hand, counts, pons);
        }

        if (labels.OffersKan)
        {
            flags |= ActionFlags.MinKan;
            AppendKanCandidate(hand, counts, kans);
        }

        if (labels.OffersChi)
        {
            flags |= ActionFlags.Chi;
            AppendChiCandidate(hand, atkValues, chis);
        }

        return new LegalActions(flags, [], pons, chis, kans);
    }

    private void AppendPonCandidate(List<Tile> hand, int[] counts, List<MeldCandidate> pons)
    {
        for (int id = 0; id < Tile.Count34; id++)
        {
            if (counts[id] < 2)
                continue;
            var claimed = Tile.FromId(id);
            var derived = CallCandidateDeriver.Derive(hand, claimed, fromSeat: 1);
            pons.AddRange(derived.Pon);
        }
    }

    /// <summary>The pon-triggering tile is published as a consecutive duplicate Int in [16..21]; scan and pick the tile_id appearing >= 2 times.</summary>
    private void AppendPonCandidateFromAtkValues(
        List<Tile> hand, int[] counts, IReadOnlyList<AtkValueRecord> atkValues, List<MeldCandidate> pons)
    {
        int scanLo = profile.AtkValues.PonClaimScanLo;
        int scanHi = profile.AtkValues.PonClaimScanHi;
        int end = Math.Min(atkValues.Count, scanHi + 1);
        Span<int> seen = stackalloc int[Tile.Count34];
        int? claimedId = null;
        for (int i = scanLo; i < end; i++)
        {
            if (atkValues[i].Type != ValueType.Int)
                continue;
            int tileId = DecodeTileId(atkValues[i].IntValue);
            if (tileId < 0)
                continue;
            seen[tileId]++;
            if (seen[tileId] >= 2)
                claimedId = tileId;
        }
        if (claimedId is null || counts[claimedId.Value] < 2)
            return;
        var claimed = Tile.FromId(claimedId.Value);
        var derived = CallCandidateDeriver.Derive(hand, claimed, fromSeat: 1);
        pons.AddRange(derived.Pon);
    }

    private void AppendKanCandidate(List<Tile> hand, int[] counts, List<MeldCandidate> kans)
    {
        for (int id = 0; id < Tile.Count34; id++)
        {
            if (counts[id] < 3)
                continue;
            var claimed = Tile.FromId(id);
            var derived = CallCandidateDeriver.Derive(hand, claimed, fromSeat: 1);
            kans.AddRange(derived.Kan);
        }
    }

    /// <summary>Try the configured chi-claim slot first; if it yields no derivable chi, scan a bounded window for any int slot that does and stop at first match.</summary>
    private void AppendChiCandidate(
        List<Tile> hand, IReadOnlyList<AtkValueRecord> atkValues, List<MeldCandidate> chis)
    {
        if (atkValues.Count == 0)
            return;

        // Exactly one claim tile per prompt — finding extra candidates inflates ChiCandidates.Count and corrupts the Pass-button index derived from it.
        int configuredIdx = profile.AtkValues.ChiClaimedTile;
        if (TryDeriveChiFromSlot(hand, atkValues, configuredIdx, chis))
            return;

        int scanLimit = Math.Min(atkValues.Count, profile.AtkValues.ChiFallbackScanLimit);
        for (int i = 0; i < scanLimit; i++)
        {
            if (i == configuredIdx)
                continue;
            if (TryDeriveChiFromSlot(hand, atkValues, i, chis))
                return;
        }
    }

    private bool TryDeriveChiFromSlot(
        List<Tile> hand, IReadOnlyList<AtkValueRecord> atkValues,
        int slot, List<MeldCandidate> chis)
    {
        if (slot < 0 || slot >= atkValues.Count)
            return false;
        if (atkValues[slot].Type != ValueType.Int)
            return false;
        int tileId = DecodeTileId(atkValues[slot].IntValue);
        if (tileId < 0)
            return false;
        var claimed = Tile.FromId(tileId);
        if (claimed.IsHonor)
            return false;
        var derived = CallCandidateDeriver.Derive(hand, claimed, fromSeat: 3);
        if (derived.Chi.Count == 0)
            return false;
        chis.AddRange(derived.Chi);
        return true;
    }

    private static ButtonLabelScan ScanButtonLabels(IReadOnlyList<AtkValueRecord> atkValues, int scanLimit)
    {
        var scan = new ButtonLabelScan();
        int end = Math.Min(atkValues.Count, scanLimit);
        for (int i = 0; i < end; i++)
        {
            var v = atkValues[i];
            if (!v.IsString || v.StringValue is null)
                continue;
            scan.RecordLabel(v.StringValue);
        }
        return scan;
    }

    private struct ButtonLabelScan
    {
        public bool OffersPon;
        public bool OffersChi;
        public bool OffersKan;
        public bool OffersRon;
        public bool OffersRiichi;
        public bool OffersTsumo;

        public bool HasAnyAcceptOffer =>
            OffersPon || OffersChi || OffersKan ||
            OffersRon || OffersRiichi || OffersTsumo;

        public void RecordLabel(string label)
        {
            switch (label)
            {
                case "Pon":
                    OffersPon = true;
                    break;
                case "Chi":
                    OffersChi = true;
                    break;
                case "Kan":
                    OffersKan = true;
                    break;
                case "Ron":
                    OffersRon = true;
                    break;
                case "Riichi":
                    OffersRiichi = true;
                    break;
                case "Tsumo":
                    OffersTsumo = true;
                    break;
            }
        }
    }

    private LegalActions BuildCallPromptLegalFromListItems(
        IReadOnlyList<Tile> hand, IReadOnlyList<AtkValueRecord> atkValues,
        IReadOnlyList<string>? listWidgetLabels)
    {
        if (listWidgetLabels is null || listWidgetLabels.Count == 0)
            return new LegalActions(ActionFlags.Pass, [], [], [], []);

        ActionFlags flags = ActionFlags.Pass;
        foreach (var raw in listWidgetLabels)
        {
            switch (raw.Trim())
            {
                case "Pon":
                    flags |= ActionFlags.Pon;
                    break;
                case "Chi":
                    flags |= ActionFlags.Chi;
                    break;
                case "Kan":
                    flags |= ActionFlags.MinKan;
                    break;
                case "Ron":
                    flags |= ActionFlags.Ron;
                    break;
                case "Riichi":
                    flags |= ActionFlags.Riichi;
                    break;
                case "Tsumo":
                    flags |= ActionFlags.Tsumo;
                    break;
            }
        }

        var pons = new List<MeldCandidate>();
        var chis = new List<MeldCandidate>();
        var kans = new List<MeldCandidate>();

        var counts = new int[Tile.Count34];
        foreach (var t in hand)
            counts[t.Id]++;

        var handList = hand as List<Tile> ?? new List<Tile>(hand);
        if ((flags & ActionFlags.Pon) != 0)
        {
            if (atkValues.Count > 0)
                AppendPonCandidateFromAtkValues(handList, counts, atkValues, pons);
            if (pons.Count == 0)
                AppendPonCandidate(handList, counts, pons);
        }
        if ((flags & ActionFlags.Chi) != 0 && atkValues.Count > 0)
            AppendChiCandidate(handList, atkValues, chis);
        if ((flags & ActionFlags.MinKan) != 0)
            AppendKanCandidate(handList, counts, kans);

        return new LegalActions(flags, [], pons, chis, kans);
    }

    private unsafe List<string> ReadVisibleListItemLabels(AtkUnitBase* unit)
    {
        var labels = new List<string>();
        if (unit == null)
            return labels;

        var host = unit->GetNodeById(profile.NodeIds.CallModalHost);
        if (host == null || (int)host->Type < 1000)
            return labels;
        var hostComp = ((AtkComponentNode*)host)->Component;
        if (hostComp == null)
            return labels;
        var shell = hostComp->GetNodeById(profile.NodeIds.CallModalShell);
        if (shell == null || (int)shell->Type < 1000)
            return labels;
        var shellComp = ((AtkComponentNode*)shell)->Component;
        if (shellComp == null)
            return labels;

        var ulm = shellComp->UldManager;
        if (ulm.NodeList == null || ulm.NodeListCount == 0)
            return labels;

        var items = new List<(float y, string text)>();
        for (int i = 0; i < ulm.NodeListCount; i++)
        {
            var node = ulm.NodeList[i];
            if (node == null || (int)node->Type < 1000 || !node->NodeFlags.HasFlag(NodeFlags.Visible))
                continue;
            var itemComp = ((AtkComponentNode*)node)->Component;
            if (itemComp == null)
                continue;
            string text = FindFirstTextInComponent(itemComp) ?? string.Empty;
            items.Add((node->Y, text));
        }
        // Top-to-bottom order matches FireCallback option index (opt 0 = top button).
        items.Sort((a, b) => a.y.CompareTo(b.y));
        foreach (var (_, t) in items)
            labels.Add(t);
        return labels;
    }

    private static unsafe string? FindFirstTextInComponent(AtkComponentBase* comp)
    {
        if (comp == null)
            return null;
        var ulm = comp->UldManager;
        if (ulm.NodeList == null || ulm.NodeListCount == 0)
            return null;
        for (int i = 0; i < ulm.NodeListCount; i++)
        {
            var node = ulm.NodeList[i];
            if (node == null || node->Type != NodeType.Text)
                continue;
            var textNode = (AtkTextNode*)node;
            var s = textNode->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    private void MaybeLogCallPromptTransition(
        VariantReadContext ctx, ReadOnlySpan<byte> memory, int stateCode,
        IReadOnlyList<AtkValueRecord> atkValues, IReadOnlyList<Tile> hand, LegalActions legal)
    {
        const ActionFlags promptFlags =
            ActionFlags.Pon | ActionFlags.Chi | ActionFlags.MinKan |
            ActionFlags.ShouMinKan | ActionFlags.Ron |
            ActionFlags.Riichi | ActionFlags.Tsumo;

        bool isPrompt = stateCode == profile.StateCodes.CallPrompt && (legal.Flags & promptFlags) != 0;
        if (!isPrompt)
        {
            lastLoggedCallPromptState = -1;
            return;
        }
        if (lastLoggedCallPromptState == profile.StateCodes.CallPrompt)
            return;
        lastLoggedCallPromptState = profile.StateCodes.CallPrompt;

        if (ctx.EventLogger is null)
            return;

        var ints = SnapshotAtkInts(atkValues, max: 24);
        ctx.EventLogger.RaiseCallPrompt(new CallPromptEvent(
            ObservedAtUtc: DateTime.UtcNow,
            AddonName: Name,
            StateCode: stateCode,
            Flags: (int)legal.Flags,
            PonClaimedTileIds: ExtractClaimedTileIds(legal.PonCandidates),
            ChiClaimedTileIds: ExtractClaimedTileIds(legal.ChiCandidates),
            KanClaimedTileIds: ExtractClaimedTileIds(legal.KanCandidates),
            IntValues: ints));

        if (!ctx.EventLogger.Enabled)
            return;

        try
        {
            System.IO.Directory.CreateDirectory(pluginConfigDir);
            var dir = pluginConfigDir;
            var path = System.IO.Path.Combine(dir, "emj-call-prompts.log");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {DateTime.UtcNow:o}  variant={Name}  state={stateCode}  atkCount={atkValues.Count}");
            sb.Append($"hand={Tiles.Render(hand)}  flags={legal.Flags}  ");
            sb.AppendLine($"pon={legal.PonCandidates.Count} chi={legal.ChiCandidates.Count} kan={legal.KanCandidates.Count}");

            int max = Math.Min(atkValues.Count, 64);
            for (int i = 0; i < max; i++)
                AppendAtkValue(sb, atkValues[i], i);

            DumpMemoryRegion(sb, memory);
            sb.AppendLine();

            System.IO.File.AppendAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            log.Error($"call-prompt diagnostic log error: {ex.Message}");
        }
    }

    private static int?[] SnapshotAtkInts(IReadOnlyList<AtkValueRecord> values, int max)
    {
        if (values.Count == 0)
            return Array.Empty<int?>();
        int n = Math.Min(values.Count, max);
        var result = new int?[n];
        for (int i = 0; i < n; i++)
        {
            var v = values[i];
            result[i] = v.Type switch
            {
                ValueType.Int => v.IntValue,
                ValueType.UInt => unchecked((int)v.UIntValue),
                ValueType.Bool => v.UIntValue != 0 ? 1 : 0,
                _ => (int?)null,
            };
        }
        return result;
    }

    private static int[] ExtractClaimedTileIds(IReadOnlyList<MeldCandidate> candidates)
    {
        if (candidates.Count == 0)
            return Array.Empty<int>();
        var ids = new int[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            ids[i] = candidates[i].ClaimedTile.Id;
        return ids;
    }

    private static void AppendAtkValue(System.Text.StringBuilder sb, AtkValueRecord v, int i)
    {
        sb.Append($"  [{i,3}] {v.Type,-14} ");
        switch (v.Type)
        {
            case ValueType.Int:
                sb.Append($"Int={v.IntValue}");
                break;
            case ValueType.UInt:
                sb.Append($"UInt={v.UIntValue} (0x{v.UIntValue:X})");
                break;
            case ValueType.Bool:
                sb.Append($"Bool={v.UIntValue != 0}");
                break;
            case ValueType.String:
            case ValueType.String8:
            case ValueType.ManagedString:
                sb.Append($"String=\"{v.StringValue ?? "(null)"}\"");
                break;
            default:
                sb.Append($"raw=0x{v.UIntValue:X}");
                break;
        }
        sb.AppendLine();
    }

    private void MaybeLogMeldTransition(
        VariantReadContext ctx, ReadOnlySpan<byte> memory, int stateCode, IReadOnlyList<Tile> hand)
    {
        if (hand.Count >= 13 || hand.Count <= 0)
        {
            lastLoggedMeldHandCount = -1;
            return;
        }
        if (hand.Count == lastLoggedMeldHandCount)
            return;
        lastLoggedMeldHandCount = hand.Count;

        if (ctx.EventLogger is null || !ctx.EventLogger.Enabled)
            return;

        try
        {
            System.IO.Directory.CreateDirectory(pluginConfigDir);
            var dir = pluginConfigDir;
            var path = System.IO.Path.Combine(dir, "emj-meld-captures.log");

            var sb = new System.Text.StringBuilder();
            int inferredMelds = (14 - hand.Count) / 3;
            int remainder = (14 - hand.Count) % 3;
            sb.AppendLine(
                $"# {DateTime.UtcNow:o}  variant={Name}  state={stateCode}  closedHand={hand.Count}  " +
                $"inferredMelds={inferredMelds}{(remainder != 0 ? " (off-sync)" : "")}  " +
                $"hand={Tiles.Render(hand)}");

            DumpAddonMeldRegion(sb, memory);
            DumpAgentEmj(sb);

            sb.AppendLine();
            System.IO.File.AppendAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            log.Error($"meld-capture diagnostic log error: {ex.Message}");
        }
    }

    private static void DumpAddonMeldRegion(System.Text.StringBuilder sb, ReadOnlySpan<byte> memory)
    {
        sb.AppendLine("  -- addon @ +0x0500..+0x3000 (per-seat blocks + post-hand area + extended) --");
        for (int off = 0x0500; off < 0x3000 && off + 16 <= memory.Length; off += 16)
            AppendHexRow(sb, memory, off, 16);
    }

    private static unsafe void DumpAgentEmj(System.Text.StringBuilder sb)
    {
        var agentModule = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentModule.Instance();
        if (agentModule == null)
        {
            sb.AppendLine("  -- AgentModule unavailable --");
            return;
        }
        var agent = agentModule->GetAgentByInternalId(
            (FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId)5);
        if (agent == null)
        {
            sb.AppendLine("  -- AgentEmj unavailable (GetAgentByInternalId returned null) --");
            return;
        }

        sb.AppendLine($"  -- AgentEmj @ 0x{(nint)agent:X} +0x0000..+0x2000 --");
        byte* agentPtr = (byte*)agent;
        for (int off = 0; off < 0x2000; off += 16)
            AppendHexRowFromPointer(sb, agentPtr, off, 16);
    }

    private static void AppendHexRow(System.Text.StringBuilder sb, ReadOnlySpan<byte> memory, int offset, int length)
    {
        sb.Append($"  +0x{offset:X4}: ");
        for (int i = 0; i < length; i++)
        {
            sb.Append($"{memory[offset + i]:X2} ");
            if (i == 7)
                sb.Append(' ');
        }
        sb.Append(" |");
        for (int i = 0; i < length; i++)
        {
            byte b = memory[offset + i];
            sb.Append(b >= 32 && b < 127 ? (char)b : '.');
        }
        sb.AppendLine("|");
    }

    private static unsafe void AppendHexRowFromPointer(System.Text.StringBuilder sb, byte* basePtr, int offset, int length)
    {
        sb.Append($"  +0x{offset:X4}: ");
        for (int i = 0; i < length; i++)
        {
            sb.Append($"{basePtr[offset + i]:X2} ");
            if (i == 7)
                sb.Append(' ');
        }
        sb.Append(" |");
        for (int i = 0; i < length; i++)
        {
            byte b = basePtr[offset + i];
            sb.Append(b >= 32 && b < 127 ? (char)b : '.');
        }
        sb.AppendLine("|");
    }

    private static void DumpMemoryRegion(System.Text.StringBuilder sb, ReadOnlySpan<byte> memory)
    {
        DumpRange(sb, memory, 0x0100, 0x0400);
        DumpRange(sb, memory, 0x0E00, 0x0400);
    }

    private static void DumpRange(System.Text.StringBuilder sb, ReadOnlySpan<byte> memory, int offset, int length)
    {
        sb.AppendLine($"  -- memory @ +0x{offset:X4}..+0x{offset + length:X4} --");
        for (int row = 0; row < length && offset + row + 16 <= memory.Length; row += 16)
            AppendHexRow(sb, memory, offset + row, 16);
    }
}
