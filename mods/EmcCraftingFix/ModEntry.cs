using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace EmcCraftingFix;

/// <summary>
/// ExtraMachineConfig transpiles <c>CraftingPage.clickCraftingRecipe</c> to pass the crafted item through its
/// <c>CraftingHarmonyPatcher.ApplyChanges(recipe, item, materialContainers)</c>. On PC the recipe is a field of the
/// method's closure (<c>ldloc.0</c>), so EMC loads it with <c>ldloc.0; ldfld recipe</c>. On Android the recipe is
/// <c>this.hoverRecipe</c>, and EMC ends up running <c>ldfld CraftingPage::hoverRecipe</c> on the closure object.
/// That reads an unrelated field, the "recipe" has a null name, and every craft throws in
/// <c>Dictionary.TryGetValue</c> before the item is made. This swaps EMC's transpiler for one that loads the recipe
/// from <c>this</c>, and still calls EMC's own <c>ApplyChanges</c>.
/// </summary>
public sealed class ModEntry : Mod
{
    private const string PatcherType = "Selph.StardewMods.ExtraMachineConfig.CraftingHarmonyPatcher";

    private static MethodInfo? applyChanges;

    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Type? patcher = AccessTools.TypeByName(PatcherType);
        if (patcher is null)
        {
            Monitor.Log("ExtraMachineConfig is not loaded; nothing to fix.", LogLevel.Trace);
            return;
        }

        MethodInfo original = AccessTools.Method(typeof(CraftingPage), "clickCraftingRecipe");
        MethodInfo? emcTranspiler = AccessTools.Method(patcher, "CraftingPage_clickCraftingRecipe_Transpiler");
        applyChanges = AccessTools.Method(patcher, "ApplyChanges");
        if (emcTranspiler is null || applyChanges is null)
        {
            Monitor.Log("ExtraMachineConfig's crafting patch has changed; leaving it alone.", LogLevel.Warn);
            return;
        }

        var harmony = new Harmony(ModManifest.UniqueID);
        harmony.Unpatch(original, emcTranspiler);
        harmony.Patch(original, transpiler: new HarmonyMethod(typeof(ModEntry), nameof(Transpiler)));
        Monitor.Log("Replaced ExtraMachineConfig's crafting transpiler with an Android-compatible one.", LogLevel.Trace);
    }

    /// <summary>After <c>closure.crafted = this.hoverRecipe.createItem()</c>, insert
    /// <c>closure.crafted = ApplyChanges(this.hoverRecipe, closure.crafted, this._materialContainers)</c>.</summary>
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo hoverRecipe = AccessTools.Field(typeof(CraftingPage), "hoverRecipe");
        FieldInfo materialContainers = AccessTools.Field(typeof(CraftingPage), "_materialContainers");
        MethodInfo createItem = AccessTools.Method(typeof(CraftingRecipe), nameof(CraftingRecipe.createItem));

        var matcher = new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld, hoverRecipe),
                new CodeMatch(OpCodes.Callvirt, createItem),
                new CodeMatch(OpCodes.Stfld))
            .ThrowIfNotMatch("Could not find hoverRecipe.createItem() in CraftingPage.clickCraftingRecipe");

        // The instruction before ldarg.0 loads the closure that the result is stored into.
        // Copy opcode and operand only: Clone() would duplicate any branch labels on it.
        CodeInstruction prev = matcher.InstructionAt(-1);
        CodeInstruction LoadClosure() => new(prev.opcode, prev.operand);
        object crafted = matcher.InstructionAt(3).operand;

        return matcher
            .Advance(4)
            .InsertAndAdvance(
                LoadClosure(),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, hoverRecipe),
                LoadClosure(),
                new CodeInstruction(OpCodes.Ldfld, crafted),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, materialContainers),
                new CodeInstruction(OpCodes.Call, applyChanges),
                new CodeInstruction(OpCodes.Stfld, crafted))
            .InstructionEnumeration();
    }
}
