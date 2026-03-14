//====================[ Imports ]====================
using System;
using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using UnityEngine;
using KeepMeAlive.Helpers;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;

namespace KeepMeAlive.Features
{
    //====================[ MedCategory ]====================
    // Logical groupings shown to the healer before they pick a specific item.
    public enum MedCategory
    {
        Bleeds,   // treats LightBleeding / HeavyBleeding
        Breaks,   // treats Fracture or DestroyedPart (splints, CMS, SURV12)
        Health,   // restores HP (medkit pool or direct HP effect)
        Comfort,  // relieves Pain / Contusion, or restores Energy / Hydration (stims, painkillers, food)
    }

    //====================[ TeamMedical ]====================
    public static class TeamMedical
    {
        //====================[ Constants & Fields ]====================
        private static float HEAL_HOLD_TIME => RevivalModSettings.TEAM_HEAL_HOLD_TIME.Value;

        //====================[ Public API ]====================
        
        // Searches a player's inventory for an item with the given instance ID. Works for both local and remote players.
        public static Item FindItemInInventory(Player player, string itemId)
        {
            try
            {
                var items = player?.Inventory?.AllRealPlayerItems;
                if (items == null)
                {
                    return null;
                }

                foreach (var item in items)
                {
                    if (item?.Id == itemId)
                    {
                        return item;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TeamMedical] FindItemInInventory error: {ex.Message}");
            }
            
            return null;
        }

        // Returns all items in the healer's inventory that can treat the patient.
        // NOTE: CanApplyItem is intentionally NOT used here — on Fika remote players the
        // health controller's synced state is often stale, causing it to return false even
        // when the patient genuinely has bleeds/fractures/etc.  Template-based classification
        // (ClassifyMed) is used instead, mirroring HealerHasMedForCategory.
        public static IEnumerable<MedsItemClass> GetUsableMeds(Player healer, Player patient)
        {
            var result = new List<MedsItemClass>();
            try
            {
                if (healer?.Inventory == null || patient == null)
                {
                    return result;
                }

                var seenTemplates = new HashSet<string>();
                foreach (var item in healer.Inventory.AllRealPlayerItems)
                {
                    if (item is not MedsItemClass meds)
                    {
                        continue;
                    }

                    // Always skip truly exhausted kits (no charges left for any category).
                    var kit = meds.GetItemComponent<MedKitComponent>();
                    if (kit != null && kit.HpResource < float.Epsilon)
                        continue;

                    // Skip items that don't map to any recognized category (e.g. purely cosmetic items).
                    if (ClassifyMed(meds).Count == 0)
                    {
                        continue;
                    }

                    // Show only one entry per item type (e.g. one bandage even if healer carries three).
                    if (!seenTemplates.Add(meds.TemplateId))
                    {
                        continue;
                    }

                    result.Add(meds);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TeamMedical] GetUsableMeds error: {ex.Message}");
            }
            
            return result;
        }

        //====================[ Category Helpers ]====================

        // Returns every MedCategory that applies to this item based on template effects.
        public static List<MedCategory> ClassifyMed(MedsItemClass med)
        {
            var categories = new List<MedCategory>();
            try
            {
                var template = med.Template as MedsTemplateClass;
                if (template == null) return categories;

                if (template.DamageEffects != null)
                {
                    if (template.DamageEffects.ContainsKey(EDamageEffectType.LightBleeding) ||
                        template.DamageEffects.ContainsKey(EDamageEffectType.HeavyBleeding))
                        categories.Add(MedCategory.Bleeds);

                    if (template.DamageEffects.ContainsKey(EDamageEffectType.Fracture) ||
                        template.DamageEffects.ContainsKey(EDamageEffectType.DestroyedPart))
                        categories.Add(MedCategory.Breaks);

                    if (template.DamageEffects.ContainsKey(EDamageEffectType.Pain) ||
                        template.DamageEffects.ContainsKey(EDamageEffectType.Contusion))
                        categories.Add(MedCategory.Comfort);
                }

                // Items with an HP resource pool (medkits) restore health.
                if (template.MaxHpResource > 0)
                {
                    categories.Add(MedCategory.Health);
                }
                else if (template.HealthEffects != null &&
                         template.HealthEffects.TryGetValue(EHealthFactorType.Health, out var hpEffect) &&
                         hpEffect != null && hpEffect.Value > 0)
                {
                    categories.Add(MedCategory.Health);
                }

                // Stimulants with a positive HealthRate buff regenerate HP over time (e.g. Propital).
                // These have no MedKitComponent so the HP-resource threshold never applies to them.
                if (!categories.Contains(MedCategory.Health))
                {
                    var stimBuffs = med.GetItemComponent<StimulatorBuffsComponent>();
                    if (stimBuffs != null)
                    {
                        foreach (var buff in stimBuffs.BuffSettings)
                        {
                            if (buff.BuffType == EStimulatorBuffType.HealthRate && buff.Value > 0)
                            {
                                categories.Add(MedCategory.Health);
                                break;
                            }
                        }
                    }
                }

                // Stimulants / food that restore Energy / Hydration or fight Poisoning → Comfort.
                // The Contains check prevents duplicate entries if Pain was already captured above.
                if (template.HealthEffects != null)
                {
                    if (template.HealthEffects.ContainsKey(EHealthFactorType.Hydration) ||
                        template.HealthEffects.ContainsKey(EHealthFactorType.Energy)    ||
                        template.HealthEffects.ContainsKey(EHealthFactorType.Poisoning))
                    {
                        if (!categories.Contains(MedCategory.Comfort))
                            categories.Add(MedCategory.Comfort);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TeamMedical] ClassifyMed error: {ex.Message}");
            }
            return categories;
        }

        // Returns usable meds for the given category only. Pass null to get all (same as GetUsableMeds).
        public static IEnumerable<MedsItemClass> GetUsableMedsByCategory(
            Player healer, Player patient, MedCategory? category)
        {
            // For the Bleeds category, detect which bleed types the patient actually has so we
            // can skip items that can't treat them (e.g. tourniquet when patient only has light
            // bleed, or bandage when patient only has heavy bleed).
            bool patientHasLight = false, patientHasHeavy = false;
            if (category == MedCategory.Bleeds)
            {
                try
                {
                    var hc = patient?.HealthController;
                    if (hc != null)
                    {
                        // GInterface339 = LightBleeding active effect
                        // GInterface340 = HeavyBleeding active effect
                        patientHasLight = hc.FindActiveEffect<GInterface339>(EBodyPart.Common) != null;
                        patientHasHeavy = hc.FindActiveEffect<GInterface340>(EBodyPart.Common) != null;
                    }
                    else { patientHasLight = patientHasHeavy = true; } // fail-open
                }
                catch { patientHasLight = patientHasHeavy = true; } // fail-open
            }

            // For the Breaks category, detect whether the patient has fractures, destroyed limbs,
            // or both. Alu splint only treats Fracture; CMS/SURV-12 treat both. Showing Alu splint
            // when every broken limb is destroyed (blacked) is misleading — it can't help.
            bool patientHasFracture = false, patientHasDestroyed = false;
            if (category == MedCategory.Breaks)
            {
                try
                {
                    var hc = patient?.HealthController;
                    if (hc != null)
                    {
                        foreach (var part in _allBodyParts)
                        {
                            if (hc.IsBodyPartBroken(part))    patientHasFracture  = true;
                            if (hc.IsBodyPartDestroyed(part)) patientHasDestroyed = true;
                            if (patientHasFracture && patientHasDestroyed) break;
                        }
                    }
                    else { patientHasFracture = patientHasDestroyed = true; } // fail-open
                }
                catch { patientHasFracture = patientHasDestroyed = true; } // fail-open
            }

            float minHp = RevivalModSettings.TEAM_HEAL_MIN_HP_RESOURCE.Value;

            foreach (var med in GetUsableMeds(healer, patient))
            {
                if (category != null && !ClassifyMed(med).Contains(category.Value))
                    continue;

                // Health category: apply the HP resource minimum threshold, but only when the
                // item does NOT also qualify via a positive HealthRate stim buff.  Items like a
                // modded Salewa (low HpResource + HealthRegeneration) must bypass the threshold
                // so they still appear even when the kit resource is nearly depleted.
                if (category == MedCategory.Health)
                {
                    var kit = med.GetItemComponent<MedKitComponent>();
                    if (kit != null && kit.HpResource < (minHp > float.Epsilon ? minHp : float.Epsilon))
                    {
                        // Allow through if a positive HealthRate buff compensates.
                        bool hasHealthRateBuff = false;
                        var stimBuffs = med.GetItemComponent<StimulatorBuffsComponent>();
                        if (stimBuffs != null)
                        {
                            foreach (var buff in stimBuffs.BuffSettings)
                            {
                                if (buff.BuffType == EStimulatorBuffType.HealthRate && buff.Value > 0)
                                {
                                    hasHealthRateBuff = true;
                                    break;
                                }
                            }
                        }
                        if (!hasHealthRateBuff) continue;
                    }
                }

                // Bleeds sub-filter: if we know exactly what the patient has, skip items
                // that can't treat it. If both or neither are known, show everything.
                if (category == MedCategory.Bleeds && (patientHasLight || patientHasHeavy))
                {
                    var template = med.Template as MedsTemplateClass;
                    if (template?.DamageEffects != null)
                    {
                        bool treatsLight = template.DamageEffects.ContainsKey(EDamageEffectType.LightBleeding);
                        bool treatsHeavy = template.DamageEffects.ContainsKey(EDamageEffectType.HeavyBleeding);
                        // Patient only has light bleed → skip items that can't treat light bleed
                        if (patientHasLight && !patientHasHeavy && !treatsLight) continue;
                        // Patient only has heavy bleed → skip items that can't treat heavy bleed
                        if (patientHasHeavy && !patientHasLight && !treatsHeavy) continue;
                    }
                }

                // Breaks sub-filter: if blacked limbs but no fractures, skip items that only
                // treat Fracture (Alu splint). If fractures but no destroyed, all items treating
                // Fracture are valid (CMS/SURV also treat fractures so nothing is hidden).
                // Show everything when both conditions exist or state is unknown (fail-open).
                if (category == MedCategory.Breaks && (patientHasFracture || patientHasDestroyed))
                {
                    var template = med.Template as MedsTemplateClass;
                    if (template?.DamageEffects != null)
                    {
                        bool treatsFracture  = template.DamageEffects.ContainsKey(EDamageEffectType.Fracture);
                        bool treatsDestroyed = template.DamageEffects.ContainsKey(EDamageEffectType.DestroyedPart);
                        // Patient only has destroyed limbs → skip items that can't treat DestroyedPart (Alu splint)
                        if (patientHasDestroyed && !patientHasFracture && !treatsDestroyed) continue;
                        // Patient only has fractures → skip items that can't treat Fracture
                        if (patientHasFracture && !patientHasDestroyed && !treatsFracture) continue;
                    }
                }

                yield return med;
            }
        }

        // Template-only check: does the healer carry at least one non-empty med that CAN treat
        // the given category — regardless of whether the patient currently needs it.
        // Used by BodyInteractable to decide which category buttons to render.
        // Intentionally skips CanApplyItem to avoid false-negatives caused by Fika
        // health-sync delay immediately after a revive.
        public static bool HealerHasMedForCategory(Player healer, MedCategory category)
        {
            try
            {
                if (healer?.Inventory == null) return false;
                foreach (var item in healer.Inventory.AllRealPlayerItems)
                {
                    if (item is not MedsItemClass meds) continue;
                    var kit = meds.GetItemComponent<MedKitComponent>();
                    if (kit != null)
                    {
                        // Always skip truly exhausted kits.
                        if (kit.HpResource < float.Epsilon) continue;
                        // For the Health category, enforce the minimum threshold — unless the item
                        // also has a positive HealthRate stim buff (e.g. modded Salewa with regen).
                        if (category == MedCategory.Health)
                        {
                            float minHp = RevivalModSettings.TEAM_HEAL_MIN_HP_RESOURCE.Value;
                            if (kit.HpResource < (minHp > float.Epsilon ? minHp : float.Epsilon))
                            {
                                bool hasHealthRateBuff = false;
                                var stimBuffs = meds.GetItemComponent<StimulatorBuffsComponent>();
                                if (stimBuffs != null)
                                {
                                    foreach (var buff in stimBuffs.BuffSettings)
                                    {
                                        if (buff.BuffType == EStimulatorBuffType.HealthRate && buff.Value > 0)
                                        {
                                            hasHealthRateBuff = true;
                                            break;
                                        }
                                    }
                                }
                                if (!hasHealthRateBuff) continue;
                            }
                        }
                    }
                    if (ClassifyMed(meds).Contains(category)) return true;
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[TeamMedical] HealerHasMedForCategory error: {ex.Message}"); }
            return false;
        }

        // Checks if the patient currently has an active condition that the given category can treat.
        // Returns true (fail-open) when the health state cannot be determined, so a category is
        // never falsely greyed out due to Fika health-sync lag or a null health controller.
        //
        //   Bleeds  → GInterface341 (LightBleeding / HeavyBleeding)
        //   Breaks  → fracture on any part (IsBodyPartBroken) OR destroyed / blacked limb (IsBodyPartDestroyed)
        //   Health  → any non-destroyed body-part with HP below maximum (medkits can treat)
        //   Comfort → catch-all: Pain (GInterface357), Contusion (GInterface352),
        //             Intoxication (GInterface346), LethalIntoxication (GInterface347),
        //             Dehydration (GInterface343), Exhaustion (GInterface344),
        //             or Hydration / Energy below 80 % of maximum.
        private static readonly EBodyPart[] _allBodyParts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
            EBodyPart.LeftArm, EBodyPart.RightArm,
            EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        public static bool PatientNeedsCategory(Player patient, MedCategory category)
        {
            try
            {
                if (patient?.HealthController == null) return true; // uncertain → fail-open
                var hc = patient.HealthController;

                switch (category)
                {
                    case MedCategory.Bleeds:
                        // GInterface341 = LightBleeding | HeavyBleeding
                        return hc.FindActiveEffect<GInterface341>(EBodyPart.Common) != null;

                    case MedCategory.Breaks:
                        // Fracture (IsBodyPartBroken) OR destroyed/blacked limb (IsBodyPartDestroyed).
                        // Surgical kits (CMS / SURV-12) treat both conditions.
                        foreach (var part in _allBodyParts)
                        {
                            if (hc.IsBodyPartBroken(part) || hc.IsBodyPartDestroyed(part))
                                return true;
                        }
                        return false;

                    case MedCategory.Health:
                        // Any limb with HP damage that a medkit can restore.
                        // Destroyed (blacked) limbs require a surgical kit → Breaks, not Health.
                        foreach (var part in _allBodyParts)
                        {
                            if (hc.IsBodyPartDestroyed(part)) continue;
                            var h = hc.GetBodyPartHealth(part);
                            if (h.Current < h.Maximum - 0.5f) return true;
                        }
                        return false;

                    case MedCategory.Comfort:
                        // --- Active damage effects ---
                        // Pain (GInterface357)
                        if (hc.FindActiveEffect<GInterface357>(EBodyPart.Common) != null) return true;
                        // Contusion (GInterface352)
                        if (hc.FindActiveEffect<GInterface352>(EBodyPart.Common) != null) return true;
                        // Intoxication / Poisoning (GInterface346)
                        if (hc.FindActiveEffect<GInterface346>(EBodyPart.Common) != null) return true;
                        // Lethal Intoxication (GInterface347)
                        if (hc.FindActiveEffect<GInterface347>(EBodyPart.Common) != null) return true;
                        // Dehydration — critically low hydration actively dealing damage (GInterface343)
                        if (hc.FindActiveEffect<GInterface343>(EBodyPart.Common) != null) return true;
                        // Exhaustion — critically low energy actively dealing damage (GInterface344)
                        if (hc.FindActiveEffect<GInterface344>(EBodyPart.Common) != null) return true;
                        // --- Stats meaningfully below maximum (healable before the damage effect fires) ---
                        if (hc.Hydration.Current < hc.Hydration.Maximum * 0.8f) return true;
                        if (hc.Energy.Current    < hc.Energy.Maximum    * 0.8f) return true;
                        return false;

                    default:
                        return true; // unknown category → fail-open
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[TeamMedical] PatientNeedsCategory error (fail-open): {ex.Message}");
                return true; // never falsely disable a category
            }
        }

        //====================[ Internal Logic ]====================
        
        // Called by MedPickerInteractable after healer selects a specific med item. Starts hold-to-heal animation and queues packet.
        public static void BeginHeal(Player healer, Player patient, MedsItemClass item)
        {
            try
            {
                if (healer == null || patient == null || item == null)
                {
                    return;
                }

                if (healer.CurrentState is not IdleStateClass)
                {
                    VFX_UI.Text(Color.yellow, "You can't heal while moving");
                    return;
                }

                VFX_UI.ObjectivePanel(Color.green, VFX_UI.Position.Default, "Healing teammate {0:F1}", HEAL_HOLD_TIME);

                var handler = new HealCompleteHandler
                {
                    healer       = healer,
                    patient      = patient,
                    healerId     = healer.ProfileId,
                    patientId    = patient.ProfileId,
                    selectedItem = item
                };

                healer.CurrentManagedState.Plant(true, false, HEAL_HOLD_TIME, handler.Complete);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TeamMedical] BeginHeal error: {ex.Message}");
            }
        }

        //====================[ HealCompleteHandler ]====================
        internal class HealCompleteHandler
        {
            //====================[ Fields ]====================
            public Player        healer;
            public Player        patient;
            public string        healerId;
            public string        patientId;
            public MedsItemClass selectedItem;

            //====================[ Callbacks ]====================
            public void Complete(bool result)
            {
                VFX_UI.HideObjectivePanel();

                if (!result)
                {
                    FikaBridge.SendTeamHealCancelPacket(patientId, healerId);
                    VFX_UI.Text(Color.yellow, "Healing cancelled!");
                    return;
                }

                // Validate patient is still alive and reachable. The 3-second hold gives enough time for them to die or disconnect.
                if (patient == null || patient.HealthController == null || !patient.HealthController.IsAlive)
                {
                    VFX_UI.Text(Color.yellow, "Patient is no longer available");
                    return;
                }

                VFX_UI.Text(Color.green, "Healing teammate...");

                // Broadcast to all machines — the patient calls ApplyItem on their side.
                // ApplyItem handles item consumption (medkit drain / single-use removal) through
                // BSG's MedEffect pipeline, synced via Fika's HealthSyncPacket.
                FikaBridge.SendTeamHealPacket(patientId, healerId, selectedItem.Id);
            }
        }
    }
}