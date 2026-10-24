using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace ItemBagsTouch;

/// <summary>The part of Item Bags' public API this mod uses.</summary>
public interface IItemBagsApi
{
    bool IsItemBag(Item item);

    void Open(Item bag, IList<Item> sourceItems, int sourceCapacity);
}

/// <summary>
/// Item Bags opens a bag on right-click, or on a left double-click that picks the bag up and
/// drops it back. Neither happens on a touch screen, so this opens a bag on a double-tap.
/// </summary>
public sealed class ModEntry : Mod
{
    private const double DoubleTapMs = 450;

    private IItemBagsApi? bags;

    private IList<Item>? lastTapList;
    private int lastTapIndex = -1;
    private DateTime lastTapTime;

    /// <summary>Opened one tick later, so the tap that triggered it is not delivered to the bag menu.</summary>
    private Action? pendingOpen;

    private bool unstowAfterBagCloses;

    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonPressed += (_, e) => TraceChestTap("press", e.Button);
        helper.Events.Input.ButtonReleased += (_, e) => TraceChestTap("release", e.Button);
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Display.MenuChanged += (_, e) => shopToFix = e.NewMenu as ShopMenu;
        // Item Bags restores one tick after SaveLoaded; put the bag ids back before that.
        helper.Events.GameLoop.Saving += (_, _) => StampBagIds();
        helper.Events.GameLoop.SaveLoaded += (_, _) =>
        {
            RestoreBagIds();
            restoreCheckTicks = RestoreCheckDelayTicks;
        };
        helper.Events.GameLoop.Saved += (_, _) => restoreCheckTicks = RestoreCheckDelayTicks;
    }

    /// <summary>
    /// On Android, Item Bags swaps every bag for a Rusty Sword with ParentSheetIndex 5000 + bag id while
    /// saving, and swaps them back after loading. Swords left over after that are bags it failed to restore.
    /// </summary>
    private const int RestoreCheckDelayTicks = 30;

    private int restoreCheckTicks = -1;

    /// <summary>
    /// SpaceCore replaces the save serializer on Android and the encoded ParentSheetIndex comes back as 0,
    /// so the bag id is also kept in modData, which survives the round trip.
    /// </summary>
    private const string BagIdKey = "thomas.ItemBagsTouch/BagId";

    private void StampBagIds()
    {
        int stamped = 0;
        foreach (Item item in FindEncodedBags())
        {
            item.modData[BagIdKey] = item.ParentSheetIndex.ToString();
            stamped++;
        }
        Monitor.Log($"Stamped {stamped} encoded bags with their id.", LogLevel.Trace);
    }

    private void RestoreBagIds()
    {
        try
        {
            int fromModData = 0;
            Utility.ForEachItem(item =>
            {
                if (item is StardewValley.Tools.MeleeWeapon
                    && item.modData.TryGetValue(BagIdKey, out string? raw)
                    && int.TryParse(raw, out int id)
                    && id >= 5000)
                {
                    item.ParentSheetIndex = id;
                    item.modData.Remove(BagIdKey);
                    fromModData++;
                }
                return true;
            });

            int fromSaveFile = RestoreInventoryIdsFromSaveFile();
            Monitor.Log($"Restored bag ids: {fromModData} from modData, {fromSaveFile} from the save file.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Restoring bag ids failed: {ex}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Fallback for saves written before the modData stamp: read the player's inventory from the save file
    /// and put back any encoded ParentSheetIndex the serializer dropped. Chests are not covered.
    /// </summary>
    private int RestoreInventoryIdsFromSaveFile()
    {
        string? folder = Constants.CurrentSavePath;
        string? name = Constants.SaveFolderName;
        if (folder == null || name == null)
            return 0;

        string path = System.IO.Path.Combine(folder, name);
        if (!System.IO.File.Exists(path))
            return 0;

        var doc = System.Xml.Linq.XDocument.Load(path);
        System.Xml.Linq.XElement? items = doc.Root?.Element("player")?.Element("items");
        if (items == null)
            return 0;

        System.Xml.Linq.XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        IList<Item> inventory = Game1.player.Items;
        int restored = 0;
        int index = 0;
        foreach (System.Xml.Linq.XElement slot in items.Elements("Item"))
        {
            int i = index++;
            if ((string?)slot.Attribute(xsi + "type") != "MeleeWeapon"
                || !int.TryParse((string?)slot.Element("parentSheetIndex"), out int id)
                || id < 5000
                || i >= inventory.Count)
                continue;

            if (inventory[i] is StardewValley.Tools.MeleeWeapon weapon && weapon.ItemId == "0" && weapon.ParentSheetIndex < 5000)
            {
                weapon.ParentSheetIndex = id;
                restored++;
            }
        }
        return restored;
    }

    private static bool IsEncodedBag(Item item)
        => item is StardewValley.Tools.MeleeWeapon && item.ParentSheetIndex >= 5000;

    private List<Item> FindEncodedBags()
    {
        var found = new List<Item>();
        Utility.ForEachItem(item =>
        {
            if (IsEncodedBag(item))
                found.Add(item);
            return true;
        });
        return found;
    }

    private void CheckBagsRestored()
    {
        IList<Item> inventory = Game1.player.Items;
        for (int i = 0; i < inventory.Count; i++)
        {
            Item? slot = inventory[i];
            if (slot != null)
                Monitor.Log($"Inventory[{i}]: {slot.GetType().FullName} name='{slot.Name}' itemId={slot.QualifiedItemId} parentSheetIndex={slot.ParentSheetIndex} isBag={bags?.IsItemBag(slot)}", LogLevel.Trace);
        }

        List<Item> encoded = FindEncodedBags();
        if (encoded.Count == 0)
        {
            Monitor.Log("All bags restored after load/save.", LogLevel.Trace);
            return;
        }

        foreach (Item item in encoded)
            Monitor.Log($"Unrestored bag: name='{item.Name}' itemId={item.QualifiedItemId} parentSheetIndex={item.ParentSheetIndex}", LogLevel.Warn);

        Type? helpers = Type.GetType("ItemBags.Helpers.SaveLoadHelpers, ItemBags");
        if (helpers == null)
        {
            Monitor.Log("ItemBags.Helpers.SaveLoadHelpers not found; cannot retry the restore.", LogLevel.Error);
            return;
        }

        Helper.Reflection.GetMethod(helpers, "LoadCustomItems").Invoke();
        int left = FindEncodedBags().Count;
        Monitor.Log($"Retried Item Bags restore: {encoded.Count - left} of {encoded.Count} bags restored.", left == 0 ? LogLevel.Info : LogLevel.Error);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        bags = Helper.ModRegistry.GetApi<IItemBagsApi>("SlayerDharok.Item_Bags");
        if (bags == null)
            Monitor.Log("Item Bags API not found; double-tap to open is disabled.", LogLevel.Error);
    }

    /// <summary>Fixed one tick after it opens, so Item Bags' own MenuChanged handler has added its stock.</summary>
    private ShopMenu? shopToFix;

    /// <summary>
    /// Item Bags adds its bags with setItemPriceAndStock, which on Android only replaces forSale. The rows
    /// stay as built: one per original item plus itemsPerPage spare rows from Initialize, which are as wide
    /// as the screen and sit over the Buy button, so tapping Buy reselects whichever bag row is behind it.
    /// The scroll limit is also left at the value computed before any stock existed.
    /// </summary>
    private void FixShopRows(ShopMenu shop)
    {
        if (Game1.activeClickableMenu != shop || shop.forSaleButtons.Count == shop.forSale.Count)
            return;

        int before = shop.forSaleButtons.Count;
        Helper.Reflection.GetMethod(shop, "rebuildSaleButtons").Invoke();

        var scrollArea = Helper.Reflection.GetField<MobileScrollbox>(shop, "scrollArea").GetValue();
        int maxOffset = Math.Max(0, (shop.forSale.Count - shop.itemsPerPage) * (shop.itemButtonHeight + 8));
        scrollArea.setMaxYOffset(maxOffset);
        scrollArea.setYOffsetForScroll(Math.Max(-maxOffset, Math.Min(0, scrollArea.getYOffsetForScroll())));
        Helper.Reflection.GetMethod(shop, "updateItemButtons").Invoke();

        Monitor.Log($"Rebuilt shop rows for {shop.ShopId}: {before} -> {shop.forSaleButtons.Count} for {shop.forSale.Count} items.", LogLevel.Trace);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (shopToFix != null)
        {
            ShopMenu shop = shopToFix;
            shopToFix = null;
            try
            {
                FixShopRows(shop);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Rebuilding shop rows failed: {ex}", LogLevel.Error);
            }
        }

        if (restoreCheckTicks >= 0 && restoreCheckTicks-- == 0)
        {
            try
            {
                CheckBagsRestored();
            }
            catch (Exception ex)
            {
                Monitor.Log($"Bag restore check failed: {ex}", LogLevel.Error);
            }
        }

        // The second toolbar tap on the selected slot stows the item; take the bag back out once its menu closes.
        if (unstowAfterBagCloses && pendingOpen == null && Game1.activeClickableMenu == null)
        {
            unstowAfterBagCloses = false;
            if (Game1.player.netItemStowed.Value)
            {
                Game1.player.netItemStowed.Set(false);
                Game1.player.UpdateItemStow();
            }
        }

        if (pendingOpen == null)
            return;

        Action open = pendingOpen;
        pendingOpen = null;
        try
        {
            open();
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to open bag: {ex}", LogLevel.Error);
        }
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (bags == null || e.Button != SButton.MouseLeft || !Context.IsWorldReady || pendingOpen != null)
            return;

        try
        {
            switch (Game1.activeClickableMenu)
            {
                case GameMenu gameMenu when gameMenu.GetCurrentPage() is InventoryPage page:
                    HandleTap(page.inventory, Game1.player.Items, Game1.player.MaxItems);
                    break;

                case ItemGrabMenu grab:
                    if (!HandleTap(grab.inventory, Game1.player.Items, Game1.player.MaxItems)
                        && grab.context is Chest
                        && grab.ItemsToGrabMenu != null)
                    {
                        HandleTap(grab.ItemsToGrabMenu, grab.ItemsToGrabMenu.actualInventory, grab.ItemsToGrabMenu.capacity);
                    }
                    break;

                case null:
                    HandleToolbarTap();
                    break;

                default:
                    ResetTap();
                    break;
            }
        }
        catch (Exception ex)
        {
            Monitor.Log($"Error handling tap: {ex}", LogLevel.Error);
        }
    }

    /// <summary>Diagnostics for bags that cannot be moved into a chest: logs the menu state around each tap.</summary>
    private void TraceChestTap(string phase, SButton button)
    {
        if (button != SButton.MouseLeft || Game1.activeClickableMenu is not ItemGrabMenu grab || grab.context is not Chest)
            return;

        // Logged before touching the menu, so a hard crash still shows whether this ran.
        Monitor.Log($"Chest {phase} begin", LogLevel.Trace);
        try
        {
            int x = Game1.getMouseX(true), y = Game1.getMouseY(true);
            Item? player = grab.inventory?.getItemAt(x, y);
            Item? chest = grab.ItemsToGrabMenu?.getItemAt(x, y);
            Monitor.Log(
                $"Chest {phase} at {x},{y}: held={grab.heldItem?.Name ?? "-"} playerSlot={player?.Name ?? "-"} chestSlot={chest?.Name ?? "-"}"
                + $" inv.selected={grab.inventory?.currentlySelectedItem} inv.drag={grab.inventory?.dragItem} chest.selected={grab.ItemsToGrabMenu?.currentlySelectedItem}"
                + $" chest.count={grab.ItemsToGrabMenu?.actualInventory.Count}/{grab.ItemsToGrabMenu?.capacity}",
                LogLevel.Trace);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Chest trace failed: {ex.Message}", LogLevel.Trace);
        }
    }

    /// <summary>Returns true when the tap landed on a slot of <paramref name="menu"/>.</summary>
    private bool HandleTap(InventoryMenu? menu, IList<Item> source, int capacity)
    {
        if (menu == null)
            return false;

        int index = SlotUnderTap(menu);
        if (index < 0 || index >= menu.actualInventory.Count)
            return false;

        if (TryRegisterDoubleTap(menu.actualInventory, index, menu.actualInventory[index]) is Item bag)
            QueueOpen(bag, source, capacity);
        return true;
    }

    /// <summary>Records a tap on a slot; returns the bag when this tap completes a double-tap on it.</summary>
    private Item? TryRegisterDoubleTap(IList<Item> list, int index, Item? item)
    {
        if (item == null || !bags!.IsItemBag(item))
        {
            ResetTap();
            return null;
        }

        DateTime now = DateTime.Now;
        bool isDoubleTap = ReferenceEquals(lastTapList, list)
            && lastTapIndex == index
            && (now - lastTapTime).TotalMilliseconds <= DoubleTapMs;

        if (!isDoubleTap)
        {
            lastTapList = list;
            lastTapIndex = index;
            lastTapTime = now;
            return null;
        }

        ResetTap();
        return item;
    }

    private void QueueOpen(Item bag, IList<Item> source, int capacity)
    {
        pendingOpen = () =>
        {
            // The game handles the same tap; only open if the bag is still in that inventory.
            if (source.Contains(bag))
                bags!.Open(bag, source, capacity);
        };
    }

    /// <summary>
    /// The Android toolbar keeps its slots in a private list; a slot's name is its offset from the
    /// first visible item (_drawStartIndex).
    /// </summary>
    private void HandleToolbarTap()
    {
        Toolbar? toolbar = null;
        foreach (IClickableMenu onScreen in Game1.onScreenMenus)
        {
            if (onScreen is Toolbar t)
            {
                toolbar = t;
                break;
            }
        }
        if (toolbar == null)
            return;

        List<ClickableComponent> buttons = Helper.Reflection.GetField<List<ClickableComponent>>(toolbar, "buttons").GetValue();
        int drawStart = Helper.Reflection.GetField<int>(toolbar, "_drawStartIndex").GetValue();

        ClickableComponent? hit = FindButton(buttons, Game1.getMouseX(true), Game1.getMouseY(true))
            ?? FindButton(buttons, Game1.getMouseX(false), Game1.getMouseY(false));
        if (hit == null || !int.TryParse(hit.name, out int offset))
        {
            ResetTap();
            return;
        }

        int index = offset + drawStart;
        IList<Item> items = Game1.player.Items;
        if (index < 0 || index >= items.Count)
        {
            ResetTap();
            return;
        }

        if (TryRegisterDoubleTap(items, index, items[index]) is Item bag)
        {
            unstowAfterBagCloses = true;
            QueueOpen(bag, items, Game1.player.MaxItems);
        }
    }

    private static ClickableComponent? FindButton(List<ClickableComponent> buttons, int x, int y)
    {
        foreach (ClickableComponent button in buttons)
        {
            if (button.containsPoint(x, y))
                return button;
        }
        return null;
    }

    /// <summary>
    /// Menus receive Game1.getMouseX/Y; which scale applies depends on UI mode, so try the UI-scaled
    /// point first and fall back to the zoom-scaled one.
    /// </summary>
    private static int SlotUnderTap(InventoryMenu menu)
    {
        int index = menu.getInventoryPositionOfClick(Game1.getMouseX(true), Game1.getMouseY(true));
        if (index >= 0)
            return index;
        return menu.getInventoryPositionOfClick(Game1.getMouseX(false), Game1.getMouseY(false));
    }

    private void ResetTap()
    {
        lastTapList = null;
        lastTapIndex = -1;
    }
}
