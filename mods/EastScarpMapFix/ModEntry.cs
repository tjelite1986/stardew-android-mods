using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace EastScarpMapFix;

/// <summary>
/// Three East Scarp maps (OrchardHouse-ruins, OrchardHouse-repaired, MeadowFarmShed_ruins) use the
/// vanilla tilesheet as <c>../TileSheets/Craftables.png</c>. SMAPI rejects any tilesheet path that climbs
/// with <c>../</c>, so those maps fail to load. It also only looks up vanilla tilesheets under
/// <c>Maps/</c>, so there is no path that reaches <c>TileSheets/Craftables</c> directly.
/// With the maps edited to <c>TileSheets/Craftables.png</c>, SMAPI asks for
/// <c>Maps/TileSheets/Craftables</c>; this provides it from the game's own texture.
/// </summary>
public sealed class ModEntry : Mod
{
    private const string AliasName = "Maps/TileSheets/Craftables";
    private const string VanillaName = "TileSheets/Craftables";

    public override void Entry(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(AliasName))
            e.LoadFrom(() => Helper.GameContent.Load<Texture2D>(VanillaName), AssetLoadPriority.Low);
    }

    /// <summary>Keep the alias in step when something re-edits the vanilla sheet (e.g. a recolor).</summary>
    private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
    {
        foreach (IAssetName name in e.NamesWithoutLocale)
        {
            if (name.IsEquivalentTo(VanillaName))
            {
                Helper.GameContent.InvalidateCache(AliasName);
                return;
            }
        }
    }
}
