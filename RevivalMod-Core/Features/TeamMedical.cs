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

        // Returns all items in the healer's inventory that the game considers applicable to the patient (HP, bleeds, fractures, pain, etc.).
        public static IEnumerable<MedsItemClass> GetUsableMeds(Player healer, Player patient)
        {
            var result = new List<MedsItemClass>();
            try
            {
                if (healer?.Inventory == null || patient?.HealthController == null)
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

                    // Skip exhausted kits.
                    var kit = meds.GetItemComponent<MedKitComponent>();
                    if (kit != null && kit.HpResource < float.Epsilon)
                    {
                        continue;
                    }

                    // Let the game decide whether this item has any applicable effect on the patient.
                    if (!patient.HealthController.CanApplyItem(meds, EBodyPart.Common))
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
            foreach (var med in GetUsableMeds(healer, patient))
            {
                if (category == null || ClassifyMed(med).Contains(category.Value))
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
                    if (kit != null && kit.HpResource < float.Epsilon) continue;
                    if (ClassifyMed(meds).Contains(category)) return true;
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[TeamMedical] HealerHasMedForCategory error: {ex.Message}"); }
            return false;
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
                FikaBridge.SendTeamHealPacket(patientId, healerId, selectedItem.Id);

                // Patient machine calls ApplyItem via packet. TeamHealRemoveItemSuppressPatch prevents local replica removal.
                // Single-use items are consumed here via Fika-synced transaction so all clients see the healer lose the item.
                bool isSingleUse = selectedItem.GetItemComponent<MedKitComponent>() == null && selectedItem.GetItemComponent<FoodDrinkComponent>() == null;
                
                if (isSingleUse)
                {
                    Utils.ConsumeMedicalItem(healer, selectedItem);
                }
            }
        }
    }
}