#!/usr/bin/env python3
"""Generate Item Bags 3.1.0 modded-bag JSON files for the tablet's content mods.

Bags select items with ItemFilters instead of item lists:
- each string in ItemFilters is ANDed with the others,
- inside one string, filters separated by "|" are ORed,
- FromMod:<prefix> matches item ids that start with "<prefix>_" or "<prefix>.".

Item Bags' menu cannot scroll, so no bag may hold more item types than fit on
screen (the built-in Food Bag holds 108). Large sets are split alphabetically
with a LocalId regex.

Usage: generate.py <output dir>
"""
import json
import os
import sys
import uuid

SIZES = ["Small", "Medium", "Large", "Giant", "Massive"]
PRICES = {"Small": 2500, "Medium": 8000, "Large": 28000, "Giant": 85000, "Massive": 400000}
CAPACITIES = {"Small": 30, "Medium": 99, "Large": 300, "Giant": 999, "Massive": 9999}

SVE = "FromMod:FlashShifter.StardewValleyExpandedCP"
# All four Cornucopia packs share the "Cornucopia_" id prefix.
CORN = "FromMod:Cornucopia"
ANY_MOD = f"{SVE}|{CORN}"

# Ids look like "Cornucopia_Name" or "FlashShifter.StardewValleyExpandedCP_Name";
# this matches the first letter after the prefix.
def first_letter(lo, hi):
    return f"LocalIdRegex:^[^_]*_[{lo}-{hi}{lo.lower()}-{hi.lower()}]"

def icon(index):
    """Rectangle of a Maps/springobjects sprite, in Item Bags' XNA string form."""
    return f"{{X:{index % 24 * 16} Y:{index // 24 * 16} Width:16 Height:16}}"

def menu(grouped, per_row=5, columns=12, slot=64):
    return {
        "GroupByQuality": grouped,
        "InventoryColumns": 12,
        "InventorySlotSize": 64,
        "GroupedLayoutOptions": {"GroupsPerRow": per_row, "ShowValueColumn": True, "SlotSize": 64},
        "UngroupedLayoutOptions": {"Columns": columns, "LineBreakIndices": [], "LineBreakHeights": [], "SlotSize": slot},
    }

def bag(file, mod_id, name, description, icon_index, seller, filters, menu_options):
    return file, {
        "IsEnabled": True,
        "ModUniqueId": mod_id,
        # Derived from the name so a regenerated file keeps its id; existing bags break if it changes.
        "BagId": str(uuid.uuid5(uuid.NAMESPACE_URL, "thomas.moddedbags/" + name)),
        "BagName": name,
        "BagDescription": description,
        "IconTexture": "SpringObjects",
        "IconPosition": icon(icon_index),
        "Prices": PRICES,
        "Capacities": CAPACITIES,
        "SizeSellers": {s: [seller] for s in SIZES},
        "SizeMenuOptions": {s: menu_options for s in SIZES},
        "Items": [],
        "ItemFilters": filters,
        "ItemFiltersSorting": "DisplayName",
    }

GROUPED = menu(True)
WIDE = menu(False, columns=20, slot=48)

BAGS = [
    bag("sve.fish.json", "FlashShifter.StardewValleyExpandedCP", "SVE Fish Bag",
        "A bag for the fish of Stardew Valley Expanded.", 128, "Willy",
        [SVE, "CategoryId:-4"], menu(True, per_row=6)),
    bag("sve.farm-forage.json", "FlashShifter.StardewValleyExpandedCP", "SVE Farm & Forage Bag",
        "A bag for Stardew Valley Expanded's crops, fruit, flowers and forage.", 16, "Pierre",
        [SVE, "CategoryId:-75,-79,-80,-81"], GROUPED),
    bag("cornucopia.vegetables.json", "Cornucopia.MoreCrops", "Cornucopia Vegetable Bag",
        "A bag for Cornucopia's vegetables.", 256, "Pierre",
        [CORN, "CategoryId:-75"], menu(True, per_row=6)),
    bag("cornucopia.fruit-greens.json", "Cornucopia.MoreCrops", "Cornucopia Fruit & Greens Bag",
        "A bag for Cornucopia's fruit and greens.", 254, "Pierre",
        [CORN, "CategoryId:-79,-81"], menu(True, per_row=6)),
    bag("cornucopia.flowers.json", "Cornucopia.MoreFlowers", "Cornucopia Flower Bag",
        "A bag for Cornucopia's flowers.", 591, "Pierre",
        [CORN, "CategoryId:-80"], menu(True, per_row=6)),
    bag("modded.seeds-a-k.json", "Cornucopia.MoreCrops", "Modded Seed Bag (A-K)",
        "A bag for seeds and saplings from Cornucopia and SVE, A to K.", 472, "Pierre",
        [ANY_MOD, "CategoryId:-74", first_letter("A", "K")], WIDE),
    bag("modded.seeds-l-z.json", "Cornucopia.MoreCrops", "Modded Seed Bag (L-Z)",
        "A bag for seeds and saplings from Cornucopia and SVE, L to Z.", 473, "Pierre",
        [ANY_MOD, "CategoryId:-74", first_letter("L", "Z")], WIDE),
    bag("modded.artisan.json", "Cornucopia.ArtisanMachines", "Modded Artisan Bag",
        "A bag for artisan goods and syrups from Cornucopia and SVE.", 348, "Pierre",
        [ANY_MOD, "CategoryId:-26,-27"], menu(True, per_row=8)),
]

for lo, hi in [("A", "C"), ("D", "L"), ("M", "R"), ("S", "Z")]:
    BAGS.append(bag(f"modded.food-{lo.lower()}-{hi.lower()}.json", "Cornucopia.CookingRecipes",
                    f"Modded Food Bag ({lo}-{hi})",
                    f"A bag for dishes from Cornucopia and SVE, {lo} to {hi}.", 194, "Gus",
                    [ANY_MOD, "CategoryId:-7", first_letter(lo, hi)], WIDE))

def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for file, data in BAGS:
        with open(os.path.join(out, file), "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
            f.write("\n")
    print(f"Wrote {len(BAGS)} bags to {out}")

if __name__ == "__main__":
    main()
