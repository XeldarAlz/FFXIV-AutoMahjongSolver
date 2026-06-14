using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoMahjongSolver.Core.Game;

/// <summary>
/// Logs the structure of the Emj addon so the hand/discard/meld node ids and the tile-texture
/// child index can be re-verified against the live client. Output goes to the Dalamud log (/xllog).
/// </summary>
internal static unsafe class EmjAddonDumper
{
    /// <summary>Log the addon's full AtkValues array (button labels + indices) for the current popup.</summary>
    public static void DumpValues()
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("values: Emj addon not present.");
            return;
        }

        LogValues(addon);
    }

    internal static void LogValues(AtkUnitBase* addon)
    {
        int count = Math.Clamp((int)addon->AtkValuesCount, 0, 64);
        int state = count > 0 ? addon->AtkValues[0].Int : -1;
        AmjsLog.Info($"AtkValues count={addon->AtkValuesCount} state={state}:");

        for (int i = 0; i < count; i++)
        {
            AtkValue v = addon->AtkValues[i];
            string typeName = v.Type.ToString();
            string label = string.Empty;
            if (typeName.Contains("String"))
            {
                string? s = v.String.ToString();
                if (!string.IsNullOrEmpty(s))
                    label = $" '{s}'";
            }

            AmjsLog.Info($"  [{i}] {typeName}={v.Int}{label}");
        }
    }

    /// <summary>
    /// Recursively log the whole Emj node tree with decoded tile textures, so the player's meld-group
    /// container node ids can be read off a live dump taken with an open meld on the board.
    /// </summary>
    public static void DumpTree()
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("tree: Emj addon not found or not visible. Open a Doman Mahjong match first.");
            return;
        }

        int state = addon->AtkValues != null ? addon->AtkValues[0].Int : -1;
        AmjsLog.Info($"Emj tree (state={state}, {TileSettings()}), NodeListCount={addon->UldManager.NodeListCount}:");
        for (int i = 0; i < addon->UldManager.NodeListCount; i++)
            DumpNode(addon->UldManager.NodeList[i], "  ", 0);
    }

    /// <summary>Current tile-appearance settings, so a tree capture can be correlated with the textures it shows.</summary>
    private static string TileSettings()
    {
        EmjModule* module = EmjModule.Instance();
        return module == null
            ? "tileSet=?"
            : $"tileSet={module->TileSet} highRes={module->ShowHighResolutionLayout}";
    }

    private static void DumpNode(AtkResNode* node, string indent, int depth)
    {
        if (node == null || depth > AmjsConstants.NodeWalkMaxDepth)
            return;

        string extra = string.Empty;
        if (node->Type == NodeType.Image)
        {
            string? path = EmjBoardReader.GetTexturePath((AtkImageNode*)node);
            if (!string.IsNullOrEmpty(path))
                extra = EmjTiles.TryFromTexturePath(path, out int tile, out bool aka)
                    ? $" tex='{path}' -> {Engine.Tiles.Name(tile)}{(aka ? " aka" : "")}"
                    : $" tex='{path}'"; // undecoded: surfaces the real path so an alternate tile set is visible
        }

        AmjsLog.Info($"{indent}id={node->NodeId} type={(int)node->Type} vis={node->IsVisible()}{extra}");

        if ((int)node->Type >= 1000)
        {
            AtkComponentBase* component = ((AtkComponentNode*)node)->Component;
            if (component != null)
            {
                AtkUldManager* uld = &component->UldManager;
                for (int i = 0; i < uld->NodeListCount; i++)
                    DumpNode(uld->NodeList[i], indent + "  ", depth + 1);
                return;
            }
        }

        for (AtkResNode* child = node->ChildNode; child != null; child = child->PrevSiblingNode)
            DumpNode(child, indent + "  ", depth + 1);
    }

    /// <summary>
    /// Focused board survey for reverse-engineering the non-hand board elements. Logs every visible
    /// node whose texture decodes to a tile and every visible non-empty text node, each with its
    /// ancestor node-id chain, then a per-container tile tally. The tally makes the four discard
    /// rivers (~6-20 tiles each), the dora indicator (1), and meld groups (3-4) easy to spot without
    /// reading a full tree dump.
    /// </summary>
    public static void DumpBoard()
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("board: Emj addon not found or not visible. Open a Doman Mahjong match first.");
            return;
        }

        int state = addon->AtkValues != null ? addon->AtkValues[0].Int : -1;
        AmjsLog.Info($"board survey (state={state}):");

        // Walk the root node only: the addon's UldManager.NodeList is flat (contains every
        // descendant), so iterating it re-roots the walk at each node and logs duplicates.
        var tally = new Dictionary<string, int>();
        SurveyNode(addon->RootNode, string.Empty, 0, tally);

        AmjsLog.Info("board: tiles per container (path prefix -> count):");
        foreach (KeyValuePair<string, int> entry in tally.OrderByDescending(e => e.Value))
            AmjsLog.Info($"  {entry.Key} -> {entry.Value} tiles");
    }

    private static void SurveyNode(AtkResNode* node, string path, int depth, Dictionary<string, int> tally)
    {
        if (node == null || depth > AmjsConstants.NodeWalkMaxDepth || !node->IsVisible())
            return;

        string here = path.Length == 0 ? node->NodeId.ToString() : $"{path}>{node->NodeId}";

        if (node->Type == NodeType.Image)
        {
            string? tex = EmjBoardReader.GetTexturePath((AtkImageNode*)node);
            if (EmjTiles.TryFromTexturePath(tex, out int tile, out bool aka))
            {
                AmjsLog.Info($"  tile {Engine.Tiles.Name(tile)}{(aka ? " aka" : "")} @ {here}");
                string container = ContainerKey(here);
                tally[container] = tally.GetValueOrDefault(container) + 1;
            }
            return;
        }

        if (node->Type == NodeType.Text)
        {
            string text = ((AtkTextNode*)node)->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                AmjsLog.Info($"  text '{text}' @ {here}");
            return;
        }

        if ((int)node->Type >= 1000)
        {
            AtkComponentBase* component = ((AtkComponentNode*)node)->Component;
            if (component != null)
            {
                AtkUldManager* uld = &component->UldManager;
                for (int i = 0; i < uld->NodeListCount; i++)
                    SurveyNode(uld->NodeList[i], here, depth + 1, tally);
                return;
            }
        }

        for (AtkResNode* child = node->ChildNode; child != null; child = child->PrevSiblingNode)
            SurveyNode(child, here, depth + 1, tally);
    }

    /// <summary>First two path segments: enough to tell apart top-level containers like rivers vs hand.</summary>
    private static string ContainerKey(string path)
    {
        int first = path.IndexOf('>');
        if (first < 0)
            return path;
        int second = path.IndexOf('>', first + 1);
        return second < 0 ? path : path[..second];
    }

    /// <summary>
    /// Log the assembled <see cref="BoardSnapshot"/> the solver would act on: rivers paired with
    /// their player panel, dora, opponent melds, and the seen-tile total. Live verification aid —
    /// in particular for the opponent river↔panel pairing, which is inferred from node-id order.
    /// </summary>
    public static void DumpState()
    {
        BoardSnapshot? board = EmjBoardReader.TryReadBoard();
        if (board is null)
        {
            AmjsLog.Warn("state: Emj addon not present.");
            return;
        }

        AmjsLog.Info($"board state (client language: {Plugin.ClientState.ClientLanguage}):");
        for (int i = 0; i < board.Rivers.Count; i++)
        {
            PlayerPanel panel = i < board.Panels.Count ? board.Panels[i] : default;
            string river = board.Rivers[i].Count == 0
                ? "(empty)"
                : string.Join(" ", board.Rivers[i].Select(Engine.Tiles.Name));
            string self = i == AmjsConstants.SelfIndex ? " (self)" : "";
            string riichi = panel.Riichi ? ", RIICHI" : "";
            string windNote = panel.Wind.Length == 0 ? " <wind unresolved>" : "";
            AmjsLog.Info($"  [{i}]{self} {panel.Name} ({panel.Wind}, {panel.Score}{riichi}){windNote}: {river}");

            if (panel.Wind.Length == 0 && i < AmjsConstants.PlayerPanelNodeIds.Length)
            {
                IReadOnlyList<string> raw = EmjBoardReader.ReadPanelTexts(AmjsConstants.PlayerPanelNodeIds[i]);
                if (raw.Count > 0)
                    AmjsLog.Info($"      raw panel texts: {string.Join(" | ", raw)}");
            }
        }

        string indicators = string.Join(" ", board.DoraIndicators.Select(Engine.Tiles.Name));
        string dora = string.Join(" ", board.DoraIndicators.Select(t => Engine.Tiles.Name(Engine.YakuCalculator.DoraFromIndicator(t))));
        AmjsLog.Info($"  dora indicator(s): {indicators} -> dora {dora}");

        if (board.OpponentMeldTiles.Count > 0)
            AmjsLog.Info($"  opponent meld tiles: {string.Join(" ", board.OpponentMeldTiles.Select(Engine.Tiles.Name))}");

        if (EmjBoardReader.TryReadHand(out HandSnapshot snap) && snap.TilesRead > 0)
        {
            int[] seen = board.BuildSeen(snap.Counts, snap.Melds);
            AmjsLog.Info($"  hand: {snap.Notation} ({snap.TilesRead} tiles, {snap.MeldCount} melds), seen tiles total: {seen.Sum()}/136");
        }
    }

    /// <summary>Log the melds currently decoded from the configured container nodes (verification aid).</summary>
    public static void DumpMelds()
    {
        if (AmjsConstants.MeldGroupNodeIds.Length == 0)
        {
            AmjsLog.Info("melds: no meld container node ids configured. Run /amjs tree with an open meld and set AmjsConstants.MeldGroupNodeIds.");
            return;
        }

        var melds = EmjBoardReader.ReadMelds();
        if (melds.Count == 0)
            AmjsLog.Info("melds: none decoded.");
        else
            AmjsLog.Info($"melds ({melds.Count}): {string.Join("  ", melds)}");
    }

    public static void Dump()
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("Emj addon not found or not visible. Open a Doman Mahjong match first.");
            return;
        }

        AmjsLog.Info($"Emj addon @ 0x{(nint)addon:X}, NodeListCount={addon->UldManager.NodeListCount}");

        foreach (uint id in AmjsConstants.HandTileNodeIds.Append(AmjsConstants.DrawnTileNodeId))
        {
            AtkResNode* node = addon->GetNodeById(id);
            if (node == null)
            {
                AmjsLog.Info($"  node {id}: <not found>");
                continue;
            }

            AmjsLog.Info($"  node {id}: type={(int)node->Type} visible={node->IsVisible()}");
            DumpComponentImages(node, indent: "    ");
        }
    }

    private static void DumpComponentImages(AtkResNode* node, string indent)
    {
        if ((int)node->Type < 1000)
            return;

        var component = ((AtkComponentNode*)node)->Component;
        if (component == null)
            return;

        // Dump the hand-tile component's own children, then drill into the button (NodeList[0]).
        DumpUldImages(&component->UldManager, indent, "hand");

        if (component->UldManager.NodeListCount > 0)
        {
            AtkResNode* button = component->UldManager.NodeList[0];
            if (button != null && (int)button->Type >= 1000)
            {
                AtkComponentBase* buttonComponent = ((AtkComponentNode*)button)->Component;
                if (buttonComponent != null)
                    DumpUldImages(&buttonComponent->UldManager, indent + "  ", "button");
            }
        }
    }

    private static void DumpUldImages(AtkUldManager* uld, string indent, string label)
    {
        if (uld == null)
            return;

        for (int i = 0; i < uld->NodeListCount; i++)
        {
            AtkResNode* child = uld->NodeList[i];
            if (child == null)
                continue;

            string extra = string.Empty;
            if (child->Type == NodeType.Image)
            {
                string? path = EmjBoardReader.GetTexturePath((AtkImageNode*)child);
                bool decoded = EmjTiles.TryFromTexturePath(path, out int tile, out bool aka);
                extra = $" tex='{path}'{(decoded ? $" -> {Engine.Tiles.Name(tile)}{(aka ? " (aka)" : "")}" : "")}";
            }

            AmjsLog.Info($"{indent}[{label} {i}] type={(int)child->Type} vis={child->IsVisible()}{extra}");
        }
    }
}
