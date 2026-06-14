using AutoMahjongSolver.Windows.Components;
using AutoMahjongSolver.Windows.Sections.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoMahjongSolver.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private enum Tab { AutoPlay, Highlight }

    private readonly record struct TabDescriptor(
        Tab Tab,
        string Label,
        FontAwesomeIcon Icon,
        string Title,
        string Subtitle,
        Action<Configuration> DrawSettings);

    private static readonly TabDescriptor[] TabDescriptors =
    {
        new(Tab.AutoPlay, "Auto-play", FontAwesomeIcon.Robot, "Auto-play",
            "How the bot paces itself while it plays for you.", AutoPlaySettings.Draw),
        new(Tab.Highlight, "Highlight", FontAwesomeIcon.Highlighter, "In-game highlight",
            "The marker drawn over the suggested discard on the live board.", HighlightSettings.Draw),
    };

    private const float SidebarWidth = 168f;
    private const float HeaderFontScale = 1.55f;

    private readonly Plugin plugin;
    private Tab activeTab = Tab.AutoPlay;

    public ConfigWindow(Plugin plugin) : base("Auto Mahjong Solver - Settings###AutoMahjongSolverConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(620, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 400),
            MaximumSize = new Vector2(2000, 1600),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var configuration = plugin.Configuration;
        using var style = Styling.PushWindowStyle();

        var sidebarWidth = SidebarWidth * ImGuiHelpers.GlobalScale;

        using (ImRaii.Child("##cfg_sidebar", new Vector2(sidebarWidth, -1), border: false))
            DrawSidebar();

        ImGui.SameLine();

        using (ImRaii.Child("##cfg_content", new Vector2(-1, -1), border: false))
            DrawContent(configuration);
    }

    private void DrawSidebar()
    {
        ImGui.Spacing();
        for (var tabIndex = 0; tabIndex < TabDescriptors.Length; tabIndex++)
        {
            var descriptor = TabDescriptors[tabIndex];
            if (SidebarTab.Draw(descriptor.Label, descriptor.Icon, Styling.Accent, activeTab == descriptor.Tab))
            {
                activeTab = descriptor.Tab;
            }
        }
    }

    private void DrawContent(Configuration configuration)
    {
        ImGui.Spacing();
        for (var tabIndex = 0; tabIndex < TabDescriptors.Length; tabIndex++)
        {
            var descriptor = TabDescriptors[tabIndex];
            if (descriptor.Tab != activeTab)
            {
                continue;
            }

            WindowHeader.Draw(descriptor.Title, descriptor.Subtitle, HeaderFontScale);
            descriptor.DrawSettings(configuration);
        }
    }
}
