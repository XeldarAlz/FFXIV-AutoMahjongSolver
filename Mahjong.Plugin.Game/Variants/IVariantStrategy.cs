namespace Mahjong.Plugin.Game.Variants;

public interface IVariantStrategy
{
    LayoutProfile Profile { get; }
}

public interface IVariantSelector
{
    IReadOnlyList<IVariantStrategy> Strategies { get; }

    IVariantStrategy? ResolveByAddonName(string addonName);
}
