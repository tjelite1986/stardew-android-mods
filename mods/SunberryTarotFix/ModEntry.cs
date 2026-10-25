using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Netcode;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SunberryTarotFix;

/// <summary>
/// Sunberry Village's <c>TarotManager</c> has a static constructor that loads
/// <c>SunberryTeam.SBV/Tarot/BuffIcons</c>. It runs on the first call into the class, which is
/// <c>TarotManager.AddEventHooks()</c> during Sunberry's Entry, before that same method registers the
/// AssetRequested handler that provides the texture. The load throws, the exception aborts Entry,
/// and every hook after it (maps, shops, special orders, Harmony patches...) is never set up.
/// This mod loads before Sunberry and provides the texture at low priority, so the static constructor
/// succeeds; once Sunberry's own handler is registered, its Exclusive load wins.
///
/// The same constructor also binds a delegate to <c>Game1.player.isMarriedOrRoommates</c>. On PC the
/// title-screen placeholder farmer already exists when mods are entered; on Android SMAPI enters mods
/// before <c>AfterLoadContent</c> calls <c>Game1.resetPlayer()</c>, so the player is null and creating
/// the delegate throws. Neither <c>resetPlayer()</c> nor a <c>Farmer</c> constructor can run that early
/// (they need the item registry and recipe data), so a Harmony prefix on Sunberry's Entry puts an
/// unconstructed placeholder in <c>Game1._player</c> and a finalizer takes it out again, so no other mod and
/// not the game ever see it. The "Sun" card's condition is then rebound to whoever <c>Game1.player</c> is
/// when it's checked (on PC it stays bound to the title-screen placeholder and is never true).
/// Requires Sunberry's manifest to list this mod as an optional dependency, so it is entered first.
/// </summary>
public sealed class ModEntry : Mod
{
    private const string SunberryId = "SunberryTeam.SBVSMAPI";
    private const string AssetName = "SunberryTeam.SBV/Tarot/BuffIcons";
    private const string RelativeFile = "assets/Tarot/Buffs.png";
    private const string LocalCopy = "assets/Buffs.png";

    public override void Entry(IModHelper helper)
    {
        IModInfo? sunberry = helper.ModRegistry.Get(SunberryId);
        if (sunberry is null)
        {
            Monitor.Log("Sunberry Village (C#) is not installed; nothing to do.", LogLevel.Trace);
            return;
        }

        // IModInfo does not expose the folder; SMAPI's implementation has a public DirectoryPath.
        string? dir = sunberry.GetType().GetProperty("DirectoryPath")?.GetValue(sunberry) as string;
        string? iconFile = dir is null ? null : FindCaseInsensitive(dir, RelativeFile);
        if (iconFile is null)
        {
            Monitor.Log($"Couldn't find {RelativeFile} in Sunberry's folder ({dir ?? "unknown"}); its code will still crash on entry.", LogLevel.Warn);
            return;
        }

        // SMAPI only loads mod files from the mod's own folder, so take a copy of Sunberry's icons.
        string copy = Path.Combine(helper.DirectoryPath, LocalCopy);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(iconFile, copy, overwrite: true);

        helper.Events.Content.AssetRequested += OnAssetRequested;

        Type? sunberryEntry = AccessTools.TypeByName("SunberryVillage.ModEntry");
        if (sunberryEntry is null)
        {
            Monitor.Log("Couldn't find SunberryVillage.ModEntry; its code will still crash on entry.", LogLevel.Warn);
            return;
        }
        new Harmony(ModManifest.UniqueID).Patch(
            AccessTools.Method(sunberryEntry, "Entry"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeSunberryEntry)),
            finalizer: new HarmonyMethod(typeof(ModEntry), nameof(AfterSunberryEntry)));
    }

    private static Farmer? placeholder;

    private static void BeforeSunberryEntry()
    {
        if (PlayerField.GetValue(null) is not null)
            return;
        placeholder = (Farmer)RuntimeHelpers.GetUninitializedObject(typeof(Farmer));
        // SMAPI's Translation reads Game1.player.Gender for gendered text whenever a player exists.
        AccessTools.Field(typeof(Farmer), nameof(Farmer.netGender)).SetValue(placeholder, new NetEnum<Gender>(Gender.Male));
        PlayerField.SetValue(null, placeholder);
    }

    private static void AfterSunberryEntry()
    {
        if (placeholder is null)
            return;
        if (ReferenceEquals(PlayerField.GetValue(null), placeholder))
            PlayerField.SetValue(null, null);
        try
        {
            RebindPlaceholderConditions(placeholder);
        }
        catch (Exception)
        {
            // TarotManager failed to initialize; let Sunberry's own exception be the one reported.
        }
        placeholder = null;
    }

    /// <summary>Replace tarot conditions bound to the placeholder; calling one on it would throw.</summary>
    private static void RebindPlaceholderConditions(Farmer stale)
    {
        Type? manager = AccessTools.TypeByName("SunberryVillage.Events.Tarot.TarotManager");
        if (manager is null || AccessTools.Field(manager, "CardPool")?.GetValue(null) is not IList cards)
            return;
        foreach (object card in cards)
        {
            FieldInfo? field = AccessTools.Field(card.GetType(), "Condition");
            if (field?.GetValue(card) is Func<bool> condition && ReferenceEquals(condition.Target, stale))
            {
                MethodInfo method = condition.Method;
                field.SetValue(card, (Func<bool>)(() => (bool)method.Invoke(Game1.player, null)!));
            }
        }
    }

    /// <summary>The backing field; the <c>Game1.player</c> setter touches the new farmer's inventory.</summary>
    private static FieldInfo PlayerField => AccessTools.Field(typeof(Game1), "_player");

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(AssetName))
            e.LoadFromModFile<Texture2D>(LocalCopy, AssetLoadPriority.Low);
    }

    /// <summary>Resolve a relative path one segment at a time, ignoring case (Android storage is case-sensitive).</summary>
    private static string? FindCaseInsensitive(string root, string relative)
    {
        string current = root;
        foreach (string part in relative.Split('/'))
        {
            string? match = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase))
                {
                    match = entry;
                    break;
                }
            }
            if (match is null)
                return null;
            current = match;
        }
        return File.Exists(current) ? current : null;
    }
}
