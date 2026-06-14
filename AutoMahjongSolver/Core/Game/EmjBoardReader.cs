using System.Numerics;
using AutoMahjongSolver.Engine;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoMahjongSolver.Core.Game;

/// <summary>A read-only snapshot of the player's closed hand and called melds parsed from the Emj addon.</summary>
internal readonly struct HandSnapshot(
    int[] counts, int[] akaByType, int tilesRead, string notation, int[] slotTiles, int[] slotIcons, IReadOnlyList<Meld> melds, int visibleNodes)
{
    public int[] Counts { get; } = counts;

    /// <summary>Red-five copies per tile type (only the 5m/5p/5s entries can be non-zero).</summary>
    public int[] AkaByType { get; } = akaByType;

    public int AkaCount => AkaByType.Sum();
    public int TilesRead { get; } = tilesRead;

    /// <summary>Hand-tile nodes that were visible (i.e. decode was attempted). Visible &gt; 0 with TilesRead == 0 means the textures weren't recognized.</summary>
    public int VisibleNodes { get; } = visibleNodes;
    public string Notation { get; } = notation;

    /// <summary>Tile index per hand slot 0-13 (node order; slot 13 = drawn tile), or -1 if empty.</summary>
    public int[] SlotTiles { get; } = slotTiles;

    /// <summary>Raw 0760xx texture icon id per hand slot, used to replay the [15, icon] highlight, or -1.</summary>
    public int[] SlotIcons { get; } = slotIcons;

    /// <summary>The player's open melds, left to right; empty for a fully concealed hand.</summary>
    public IReadOnlyList<Meld> Melds { get; } = melds;

    /// <summary>Number of completed called sets, which the solver treats as fixed blocks.</summary>
    public int MeldCount => Melds.Count;
}

/// <summary>A selectable button in a call / self-action popup and the [11, Option] value that fires it.</summary>
internal readonly record struct ActionOption(string Label, int Option);

/// <summary>One seat's info panel: name, seat wind text, score, and whether a Riichi! badge is up.</summary>
internal readonly record struct PlayerPanel(string Name, string Wind, int Score, bool Riichi);

/// <summary>
/// Everything visible on the table beyond our own hand: the four rivers, dora indicators, opponent
/// meld tiles, and per-seat panels. Index 0 of <see cref="Rivers"/>/<see cref="Panels"/> is the
/// local player; the opponent river↔panel pairing follows node-id order (pending live confirmation).
/// </summary>
internal sealed class BoardSnapshot
{
    public required IReadOnlyList<IReadOnlyList<int>> Rivers { get; init; }
    public required IReadOnlyList<int> DoraIndicators { get; init; }
    public required IReadOnlyList<int> OpponentMeldTiles { get; init; }
    public required IReadOnlyList<PlayerPanel> Panels { get; init; }

    public IReadOnlyList<int> OwnRiver => Rivers[AmjsConstants.SelfIndex];

    public bool OwnRiichi => Panels[AmjsConstants.SelfIndex].Riichi;

    public bool AnyOpponentRiichi
    {
        get
        {
            for (int i = 0; i < Panels.Count; i++)
                if (i != AmjsConstants.SelfIndex && Panels[i].Riichi)
                    return true;
            return false;
        }
    }

    /// <summary>Rivers of every opponent currently in riichi (the discards that are 100% safe against them).</summary>
    public List<IReadOnlyList<int>> ThreatRivers
    {
        get
        {
            var threats = new List<IReadOnlyList<int>>();
            for (int i = 0; i < Panels.Count && i < Rivers.Count; i++)
                if (i != AmjsConstants.SelfIndex && Panels[i].Riichi)
                    threats.Add(Rivers[i]);
            return threats;
        }
    }

    /// <summary>Copies of each tile type visible anywhere: our hand and melds, all rivers, dora indicators, opponent melds.</summary>
    public int[] BuildSeen(int[] concealedCounts, IReadOnlyList<Meld> melds)
    {
        var seen = new int[Tiles.TypeCount];
        Array.Copy(concealedCounts, seen, Tiles.TypeCount);

        foreach (Meld meld in melds)
            foreach (int t in meld.Tiles)
                seen[t]++;
        foreach (IReadOnlyList<int> river in Rivers)
            foreach (int t in river)
                seen[t]++;
        foreach (int t in DoraIndicators)
            seen[t]++;
        foreach (int t in OpponentMeldTiles)
            seen[t]++;

        for (int t = 0; t < Tiles.TypeCount; t++)
            seen[t] = Math.Min(seen[t], 4);
        return seen;
    }
}

/// <summary>
/// Read-only observer of the Doman Mahjong "Emj" addon. Phase 1: parse the player's hand by
/// walking the known hand-tile nodes and decoding each tile from its image texture id.
/// Everything here is best-effort and gated on node visibility; node ids need live re-verification.
/// </summary>
internal static unsafe class EmjBoardReader
{
    public static bool TryGetAddon(out AtkUnitBase* addon)
    {
        addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(AmjsConstants.EmjAddon, 1).Address;
        return addon != null && addon->IsVisible;
    }

    /// <summary>The addon's AtkValues[0] state code (30 = our discard turn), or -1 if unavailable.</summary>
    public static int ReadStateCode()
    {
        if (!TryGetAddon(out AtkUnitBase* addon))
            return -1;
        return addon->AtkValues != null ? addon->AtkValues[0].Int : -1;
    }

    /// <summary>Read a string AtkValue by index (e.g. the offered-action label at [7]), or null.</summary>
    public static string? ReadActionLabel(int index)
    {
        if (!TryGetAddon(out AtkUnitBase* addon) || addon->AtkValues == null)
            return null;
        if (index < 0 || index >= addon->AtkValuesCount)
            return null;

        AtkValue v = addon->AtkValues[index];
        if (!v.Type.ToString().Contains("String"))
            return null;
        return v.String.ToString();
    }

    /// <summary>
    /// The action buttons offered by the current call / self-action popup, left to right. Labels live in
    /// consecutive string AtkValues from <see cref="AmjsConstants.ActionButtonBaseIndex"/>; a button's
    /// fire value is its position in that run ([7]->0, [8]->1, ...). Empty on an ordinary discard turn.
    /// </summary>
    public static IReadOnlyList<ActionOption> ReadActionOptions()
    {
        if (!TryGetAddon(out AtkUnitBase* addon) || addon->AtkValues == null)
            return Array.Empty<ActionOption>();

        var options = new List<ActionOption>();
        for (int i = AmjsConstants.ActionButtonBaseIndex; i < addon->AtkValuesCount; i++)
        {
            AtkValue v = addon->AtkValues[i];
            if (!v.Type.ToString().Contains("String"))
                break;

            string? label = v.String.ToString();
            if (string.IsNullOrEmpty(label))
                break;

            options.Add(new ActionOption(label, i - AmjsConstants.ActionButtonBaseIndex));
        }

        return options;
    }

    public static bool TryReadHand(out HandSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetAddon(out AtkUnitBase* addon))
            return false;

        var counts = new int[Tiles.TypeCount];
        var akaByType = new int[Tiles.TypeCount];
        var slots = new int[14];
        var icons = new int[14];
        Array.Fill(slots, -1);
        Array.Fill(icons, -1);
        int read = 0;
        int visible = 0;

        for (int i = 0; i < 14; i++)
        {
            uint id = i < AmjsConstants.HandTileNodeIds.Length
                ? AmjsConstants.HandTileNodeIds[i]
                : AmjsConstants.DrawnTileNodeId;

            AtkResNode* node = addon->GetNodeById(id);
            if (node == null || !node->IsVisible())
                continue;

            visible++;
            string? path = ReadHandTileTexture(node);
            if (EmjTiles.TryIconId(path, out int iconId) && EmjTiles.TryFromIconId(iconId, out int tile, out bool isAka))
            {
                counts[tile]++;
                slots[i] = tile;
                icons[i] = iconId;
                if (isAka) akaByType[tile]++;
                read++;
            }
        }

        snapshot = new HandSnapshot(counts, akaByType, read, ToNotation(counts), slots, icons, ReadMelds(addon), visible);
        return read > 0;
    }

    /// <summary>
    /// Screen-space bounding box of a hand slot's tile node (0-12 = closed hand left to right, 13 = the
    /// drawn tile), for painting an overlay over the live board. Mirrors <see cref="TryReadHand"/>'s node
    /// mapping. False when the addon or that node is not currently on screen.
    /// </summary>
    public static bool TryGetHandSlotRect(int slot, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;
        if (slot is < 0 or > 13 || !TryGetAddon(out AtkUnitBase* addon))
            return false;

        uint id = slot < AmjsConstants.HandTileNodeIds.Length
            ? AmjsConstants.HandTileNodeIds[slot]
            : AmjsConstants.DrawnTileNodeId;

        AtkResNode* node = addon->GetNodeById(id);
        if (node == null || !node->IsVisible())
            return false;

        Vector2 scale = NodeScale(node);
        min = new Vector2(node->ScreenX, node->ScreenY);
        max = min + new Vector2(node->Width * scale.X, node->Height * scale.Y);
        return true;
    }

    /// <summary>Cumulative render scale of a node: its own scale times every ancestor's, up to the addon root.</summary>
    private static Vector2 NodeScale(AtkResNode* node)
    {
        var scale = new Vector2(node->ScaleX, node->ScaleY);
        for (AtkResNode* parent = node->ParentNode; parent != null; parent = parent->ParentNode)
            scale *= new Vector2(parent->ScaleX, parent->ScaleY);
        return scale;
    }

    /// <summary>
    /// Read the whole visible table state. Best-effort: returns null when the addon is not up;
    /// individual elements degrade to empty lists if their nodes are missing.
    /// </summary>
    public static BoardSnapshot? TryReadBoard()
    {
        if (!TryGetAddon(out AtkUnitBase* addon))
            return null;

        var rivers = new IReadOnlyList<int>[AmjsConstants.RiverNodeIds.Length];
        for (int i = 0; i < rivers.Length; i++)
            rivers[i] = ReadRiver(addon, AmjsConstants.RiverNodeIds[i].FirstSlot);

        var dora = new List<int>();
        CollectTiles(addon->GetNodeById(AmjsConstants.DoraGroupNodeId), dora, depth: 0);

        var meldTiles = new List<int>();
        foreach (uint id in AmjsConstants.OpponentMeldAreaNodeIds)
            CollectTiles(addon->GetNodeById(id), meldTiles, depth: 0);

        var panels = new PlayerPanel[AmjsConstants.PlayerPanelNodeIds.Length];
        for (int i = 0; i < panels.Length; i++)
            panels[i] = ReadPanel(addon, AmjsConstants.PlayerPanelNodeIds[i]);

        return new BoardSnapshot
        {
            Rivers = rivers,
            DoraIndicators = dora,
            OpponentMeldTiles = meldTiles,
            Panels = panels,
        };
    }

    /// <summary>Read one discard river in order: slot 0 is FirstSlot, slot k is FirstSlot*10000+k.</summary>
    private static IReadOnlyList<int> ReadRiver(AtkUnitBase* addon, uint firstSlot)
    {
        var tiles = new List<int>();
        var slotTiles = new List<int>();

        for (int k = 0; k <= AmjsConstants.RiverMaxSlots; k++)
        {
            uint id = k == 0 ? firstSlot : firstSlot * 10000u + (uint)k;
            AtkResNode* node = addon->GetNodeById(id);
            if (node == null || !node->IsVisible())
                break;

            slotTiles.Clear();
            CollectTiles(node, slotTiles, depth: 0);
            if (slotTiles.Count == 0)
                break;

            tiles.Add(slotTiles[0]);
        }

        return tiles;
    }

    /// <summary>
    /// Parse a seat's info panel from its text nodes: the wind is the one compass word, the score
    /// the largest digit-only run (the panel duplicates it with a leading zero), the riichi state a
    /// "Riichi" badge, and the name the first remaining non-badge text.
    /// </summary>
    private static PlayerPanel ReadPanel(AtkUnitBase* addon, uint id)
    {
        var texts = new List<string>();
        CollectTexts(addon->GetNodeById(id), texts, depth: 0);

        string name = string.Empty;
        string wind = string.Empty;
        int score = 0;
        bool riichi = false;

        foreach (string text in texts)
        {
            if (EmjStrings.CanonicalWind(text) is { } seatWind)
            {
                if (wind.Length == 0)
                    wind = seatWind;
            }
            else if (EmjStrings.IsRiichi(text))
            {
                riichi = true;
            }
            else if (text.Length > 0 && text.All(char.IsDigit))
            {
                if (int.TryParse(text, out int value) && value > score)
                    score = value;
            }
            else if (name.Length == 0 && !text.Contains('!') && !text.StartsWith('+') && !text.StartsWith('-'))
            {
                name = text;
            }
        }

        return new PlayerPanel(name, wind, score, riichi);
    }

    /// <summary>Raw, unclassified text nodes under a seat panel — used to capture localized strings for verification.</summary>
    public static IReadOnlyList<string> ReadPanelTexts(uint panelId)
    {
        if (!TryGetAddon(out AtkUnitBase* addon))
            return Array.Empty<string>();
        var texts = new List<string>();
        CollectTexts(addon->GetNodeById(panelId), texts, depth: 0);
        return texts;
    }

    /// <summary>Walk a node subtree, appending every visible non-empty text node's content.</summary>
    private static void CollectTexts(AtkResNode* node, List<string> texts, int depth)
    {
        if (node == null || depth > AmjsConstants.NodeWalkMaxDepth || !node->IsVisible())
            return;

        if (node->Type == NodeType.Text)
        {
            string text = ((AtkTextNode*)node)->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(text.Trim());
            return;
        }

        if ((int)node->Type >= 1000)
        {
            AtkComponentBase* component = ((AtkComponentNode*)node)->Component;
            if (component != null)
            {
                AtkUldManager* uld = &component->UldManager;
                for (int i = 0; i < uld->NodeListCount; i++)
                    CollectTexts(uld->NodeList[i], texts, depth + 1);
                return;
            }
        }

        for (AtkResNode* child = node->ChildNode; child != null; child = child->PrevSiblingNode)
            CollectTexts(child, texts, depth + 1);
    }

    /// <summary>
    /// Parse the player's open melds from their container nodes. Each container's decoded tiles are
    /// classified into a single chii/pon/kan; groups that do not form a valid meld are skipped.
    /// </summary>
    public static IReadOnlyList<Meld> ReadMelds()
        => TryGetAddon(out AtkUnitBase* addon) ? ReadMelds(addon) : Array.Empty<Meld>();

    private static IReadOnlyList<Meld> ReadMelds(AtkUnitBase* addon)
    {
        if (AmjsConstants.MeldGroupNodeIds.Length == 0)
            return Array.Empty<Meld>();

        var melds = new List<Meld>();
        var tiles = new List<int>();

        foreach (uint id in AmjsConstants.MeldGroupNodeIds)
        {
            AtkResNode* container = addon->GetNodeById(id);
            if (container == null || !container->IsVisible())
                continue;

            tiles.Clear();
            CollectTiles(container, tiles, depth: 0);

            if (Meld.TryClassify(tiles, out Meld meld))
                melds.Add(meld);
        }

        return melds;
    }

    /// <summary>Walk a node subtree, appending the tile index of every decodable tile texture found.</summary>
    private static void CollectTiles(AtkResNode* node, List<int> tiles, int depth)
    {
        if (node == null || depth > AmjsConstants.NodeWalkMaxDepth || !node->IsVisible())
            return;

        if (node->Type == NodeType.Image)
        {
            string? path = GetTexturePath((AtkImageNode*)node);
            if (EmjTiles.TryFromTexturePath(path, out int tile, out _))
                tiles.Add(tile);
            return;
        }

        if ((int)node->Type >= 1000)
        {
            AtkComponentBase* component = ((AtkComponentNode*)node)->Component;
            if (component != null)
            {
                AtkUldManager* uld = &component->UldManager;
                for (int i = 0; i < uld->NodeListCount; i++)
                    CollectTiles(uld->NodeList[i], tiles, depth + 1);
                return;
            }
        }

        for (AtkResNode* child = node->ChildNode; child != null; child = child->PrevSiblingNode)
            CollectTiles(child, tiles, depth + 1);
    }

    /// <summary>Hand tile node -> button (NodeList[0]) -> image (NodeList[ImageIndex]) -> texture path.</summary>
    private static string? ReadHandTileTexture(AtkResNode* node)
    {
        if ((int)node->Type < 1000)
            return null; // not a component node

        var component = ((AtkComponentNode*)node)->Component;
        if (component == null)
            return null;

        AtkResNode* button = GetChildFromUld(&component->UldManager, AmjsConstants.HandButtonNodeListIndex);
        if (button == null)
            return null;

        AtkComponentBase* buttonComponent = ((AtkComponentNode*)button)->Component;
        if (buttonComponent == null)
            return null;

        AtkResNode* image = GetChildFromUld(&buttonComponent->UldManager, AmjsConstants.HandImageNodeListIndex);
        if (image == null)
            return null;

        return GetTexturePath((AtkImageNode*)image);
    }

    private static AtkResNode* GetChildFromUld(AtkUldManager* uld, int index)
    {
        if (uld == null || index < 0 || index >= uld->NodeListCount)
            return null;
        return uld->NodeList[index];
    }

    public static string? GetTexturePath(AtkImageNode* node)
    {
        if (node == null)
            return null;

        AtkUldPartsList* partsList = node->PartsList;
        if (partsList == null)
            return null;

        uint partId = node->PartId;
        if (partId >= partsList->PartCount)
            return null;

        AtkUldAsset* asset = partsList->Parts[partId].UldAsset;
        if (asset == null || asset->AtkTexture.TextureType != TextureType.Resource)
            return null;

        var resource = asset->AtkTexture.Resource;
        if (resource == null)
            return null;

        var handle = resource->TexFileResourceHandle;
        if (handle == null)
            return null;

        return handle->ResourceHandle.FileName.ToString();
    }

    private static string ToNotation(int[] counts)
    {
        var sb = new System.Text.StringBuilder();
        AppendSuit(sb, counts, 0, 'm');
        AppendSuit(sb, counts, 9, 'p');
        AppendSuit(sb, counts, 18, 's');

        var honors = new System.Text.StringBuilder();
        for (int r = 0; r < 7; r++)
            for (int n = 0; n < counts[27 + r]; n++)
                honors.Append((char)('1' + r));
        if (honors.Length > 0)
            sb.Append(honors).Append('z');

        return sb.ToString();
    }

    private static void AppendSuit(System.Text.StringBuilder sb, int[] counts, int start, char suit)
    {
        bool any = false;
        for (int r = 0; r < 9; r++)
            for (int n = 0; n < counts[start + r]; n++)
            {
                sb.Append((char)('1' + r));
                any = true;
            }
        if (any)
            sb.Append(suit);
    }
}
