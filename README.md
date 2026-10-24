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

Requires Item Bags (`SlayerDharok.Item_Bags`). Talks to it only through its public API
(`IItemBagsApi`), so it does not reference `ItemBags.dll`.

### Building

The project references the Android game and SMAPI assemblies, which are not in this repo. Copy them
from the device's `files/Stardew Assemblies/` into `mods/ItemBagsTouch/lib/`:

```
StardewValley.dll  StardewValley.GameData.dll  StardewModdingAPI.dll
SMAPI.Toolkit.CoreInterfaces.dll  MonoGame.Framework.dll  xTile.dll
```

Then:

```sh
cd mods/ItemBagsTouch
dotnet build -c Release
```

Copy `bin/Release/net9.0/{ItemBagsTouch.dll,manifest.json}` to
`/sdcard/Android/data/abc.smapi.gameloader/files/Mods/ItemBagsTouch/`.
