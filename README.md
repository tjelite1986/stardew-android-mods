# Stardew Valley Android mods

SMAPI mods written to make PC mods usable on Android (SMAPI Launcher).

## ItemBagsTouch

Companion mod for [Item Bags](https://www.nexusmods.com/stardewvalley/mods/1804) 3.1.0 on Android.

- **Double-tap to open a bag** in the inventory page, in chests and in the toolbar. Item Bags only
  opens bags on right-click, which a touch screen does not have.
- **Keeps bags from turning into Rusty Swords after save/reload.** Item Bags swaps each bag for a
  Rusty Sword with `ParentSheetIndex = 5000 + bag id` while saving and swaps it back after loading.
  On Android, SpaceCore's serializer drops that index, so the bag is lost. This mod stores the bag id
  in `modData` on `Saving` and restores it on `SaveLoaded`, before Item Bags runs its own restore.
- **Keeps a chest's empty slots after a bag closes.** Item Bags removes every empty slot from the
  chest before showing it again. On Android you drop items into those empty slots, so the chest looked
  sorted and nothing more could go in until it was reopened. A Harmony patch on `ItemBag.CloseContents`
  puts the empty slots back where they were.

Requires Item Bags (`SlayerDharok.Item_Bags`). Talks to it through its public API
(`IItemBagsApi`) and one Harmony patch found by name, so it does not reference `ItemBags.dll`.

### Building

The project references the Android game and SMAPI assemblies, which are not in this repo. Copy them
from the device's `files/Stardew Assemblies/` into `mods/ItemBagsTouch/lib/`:

```
StardewValley.dll  StardewValley.GameData.dll  StardewModdingAPI.dll
SMAPI.Toolkit.CoreInterfaces.dll  MonoGame.Framework.dll  xTile.dll  0Harmony.dll
```

Then:

```sh
cd mods/ItemBagsTouch
dotnet build -c Release
```

Copy `bin/Release/net9.0/{ItemBagsTouch.dll,manifest.json}` to
`/sdcard/Android/data/abc.smapi.gameloader/files/Mods/ItemBagsTouch/`.

## MapPreload

Creating a new game builds every location in `Data/Locations`, and each one loads its map on the main
thread. With Stardew Valley Expanded installed that is ~350 maps and ~11 seconds without handling
input, so Android shows its "app isn't responding" dialog (the limit is 10 seconds). Only the first
new game after starting the app is affected; after that the maps are cached.

This mod loads the same maps into the same content manager (`Game1.game1.xTileContent`) while the
title screen is open, a few per tick (12 ms budget), starting 3 seconds after the title appears so
Content Patcher has finished its own setup. The SMAPI log shows `Preloaded N maps ... in X ms`
when it is done. Content Patcher still invalidates any map whose patches change once the save
exists, so the cached copies never hide a patch.

No dependencies. Build and copy it like ItemBagsTouch, into `Mods/MapPreload/`.

## EmcCraftingFix

With ExtraMachineConfig 1.17.1-unofficial.1-android
installed, nothing can be crafted on Android: every recipe throws
`ArgumentNullException: Value cannot be null. (Parameter 'key')` from
`CraftingHarmonyPatcher.ApplyChanges`, and the tap falls through to the game (it zooms instead).

EMC's transpiler for `CraftingPage.clickCraftingRecipe` assumes the PC layout, where the recipe is a
field of the method's closure (`ldloc.0`). On Android the recipe is `this.hoverRecipe`, so EMC reads
`hoverRecipe` off the closure object and gets a recipe with a null name. This mod removes EMC's
transpiler on `GameLaunched` and applies one that loads the recipe from `this`, still calling EMC's own
`ApplyChanges`. It does nothing if ExtraMachineConfig is not installed.

Build and copy it like ItemBagsTouch, into `Mods/EmcCraftingFix/`.

## Modded bags for Item Bags

Item Bags' built-in bags only hold vanilla items. `modded-bags/generate.py <dir>` writes 12 modded-bag
definitions for Stardew Valley Expanded and the Cornucopia packs (`modded-bags/out/`). Copy them to
`Mods/ItemBags/assets/Modded Bags/`.

- Bags select items with `ItemFilters`: the entries are ANDed and `|` ORs inside one entry.
  `FromMod:Cornucopia` covers all four Cornucopia packs (their ids share the `Cornucopia_` prefix).
- The bag menu cannot scroll, so big sets are split by first letter (the built-in Food Bag holds 108 types).
- `BagId` is a UUIDv5 of the bag name, so regenerating keeps existing bags. Renaming a bag orphans them.

## Configs

Config files as they are on the tablet, for mods that needed changes:

- `configs/OddQualityOfLife/config.json`: friendship/animal/fence decay, pet bowls and fishing hook assist
  are off, because CJB Cheats Menu and Yet Another Fishing Mod already handle them. The `*_decay` keys
  read backwards: `true` turns ON the feature that prevents decay.
- `configs/VirtualKeyboard/config.json`: `Position` reset to `60, 20`. The mod reads the saved position as
  screen pixels and rescales it on every start, so it drifts until the button is off-screen. Reset it
  when the button disappears.
