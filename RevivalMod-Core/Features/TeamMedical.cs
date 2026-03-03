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
    //====================[ TeamMedical ]====================
    public static class TeamMedical
    {
        //====================[ Constants ]====================
        private const float HEAL_HOLD_TIME = 3f;

        //====================[ Public API ]====================

        /// <summary>
        /// Searches a player's inventory for an item with the given instance ID.
        /// Works for both local players and observed (remote) players.
        /// </summary>
        public static Item FindItemInInventory(Player player, string itemId)
        {
            try
            {
                var items = player?.Inventory?.AllRealPlayerItems;
                if (items == null) return null;

                foreach (var item in items)
                    if (item?.Id == itemId) return item;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TeamMedical] FindItemInInventory error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Returns all items in the healer's inventory that the game considers applicable to the patient.
        /// Uses CanApplyItem(EBodyPart.Common) which covers HP, bleeds, fractures, pain — everything
        /// that double-clicking the item in native Tarkov would handle.
        /// </summary>
        public static IEnumerable<MedsItemClass> GetUsableMeds(Player healer, Player patient)
        {
            var result = new List<MedsItemClass>();
            try
            {
                if (healer?.Inventory == null || patient?.HealthController == null) return result;

                var seenTemplates = new System.Collections.Generic.HashSet<string>();
                foreach (var item in healer.Inventory.AllRealPlayerItems)
                {
                    if (item is not MedsItemClass meds) continue;

                    // Skip exhausted kits.
                    var kit = meds.GetItemComponent<MedKitComponent>();
                    if (kit != null && kit.HpResource < float.Epsilon) continue;

                    // Let the game decide whether this item has any applicable effect on the patient.
                    if (!patient.HealthController.CanApplyItem(meds, EBodyPart.Common)) continue;

                    // Show only one entry per item type (e.g. one bandage even if healer carries three).
                    if (!seenTemplates.Add(meds.TemplateId)) continue;

                    result.Add(meds);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TeamMedical] GetUsableMeds error: {ex.Message}");
            }
            return result;
        }

        //====================[ Internal Logic ]====================

        /// <summary>
        /// Called by MedPickerInteractable after the healer selects a specific med item.
        /// Starts the hold-to-heal animation and queues the heal packet on completion.
        /// </summary>
        public static void BeginHeal(Player healer, Player patient, MedsItemClass item)
        {
            try
            {
                if (healer == null || patient == null || item == null) return;

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
            public Player       healer;
            public Player       patient;
            public string       healerId;
            public string       patientId;
            public MedsItemClass selectedItem;

            public void Complete(bool result)
            {
                VFX_UI.HideObjectivePanel();

                if (!result)
                {
                    FikaBridge.SendTeamHealCancelPacket(patientId, healerId);
                    VFX_UI.Text(Color.yellow, "Healing cancelled!");
                    return;
                }

                VFX_UI.Text(Color.green, "Healing teammate...");

                // Broadcast to all machines — the patient calls ApplyItem on their side.
                FikaBridge.SendTeamHealPacket(patientId, healerId, selectedItem.Id);

                if (patient != null && patient.IsYourPlayer)
                {
                    // Edge case: patient is on the same machine as the healer.
                    // ApplyItem handles item removal natively via GClass3017.RemoveItem
                    // (owner is ClientInventoryController, not ObsCtrl, so our patch doesn't suppress it).
                    patient.HealthController.ApplyItem(selectedItem, EBodyPart.Common);
                }
                else
                {
                    // Remote patient: the patient machine will call ApplyItem via the packet.
                    // Our TeamHealRemoveItemSuppressPatch prevents the local-only replica removal
                    // that would otherwise leave the healer's real inventory out of sync.
                    // For single-use items (bandage, splint, tourniquet — no MedKitComponent or
                    // FoodDrinkComponent) we consume them here via a Fika-synced transaction so
                    // every client correctly sees the healer lose the item.
                    bool isSingleUse = selectedItem.GetItemComponent<MedKitComponent>() == null
                                    && selectedItem.GetItemComponent<FoodDrinkComponent>() == null;
                    if (isSingleUse)
                        Utils.ConsumeMedicalItem(healer, selectedItem);
                }
            }
        }
    }
}