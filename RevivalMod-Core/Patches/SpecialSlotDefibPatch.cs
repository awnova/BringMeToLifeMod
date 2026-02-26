using System;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using RevivalMod.Helpers;
using SPT.Reflection.Patching;

namespace RevivalMod.Patches;

/// <summary>
/// Allows the configured revival item (defib) to be placed in SpecialSlots.
/// The game's Slot.CheckCompatibility uses Filters.CheckItemFilter; server DB patches may not
/// reach the client in time or may be overridden. This client-side patch forces compatibility
/// when the slot is a SpecialSlot and the item is the revival item.
/// </summary>
public class SpecialSlotDefibPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(Slot), nameof(Slot.CheckCompatibility));

    [PatchPostfix]
    private static void Postfix(Slot __instance, Item item, ref bool __result)
    {
        if (__result) return; // Already compatible

        if (item == null) return;

        if (!__instance.IsSpecial) return; // Not a SpecialSlot

        var revivalTpl = RevivalModSettings.REVIVAL_ITEM_ID?.Value ?? "5c052e6986f7746b207bc3c9";
        if (string.IsNullOrEmpty(revivalTpl)) return;

        var itemTpl = item.StringTemplateId ?? (string)item.TemplateId;
        if (string.IsNullOrEmpty(itemTpl)) return;

        if (string.Equals(itemTpl, revivalTpl, System.StringComparison.OrdinalIgnoreCase))
        {
            __result = true;
        }
    }
}
