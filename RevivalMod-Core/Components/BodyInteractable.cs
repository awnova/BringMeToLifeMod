//====================[ Imports ]====================
using EFT;
using EFT.Interactive;
using EFT.UI;
using System;
using UnityEngine;
using KeepMeAlive.Helpers;
using KeepMeAlive.Features;
using KeepMeAlive.Fika;

namespace KeepMeAlive.Components
{
    //====================[ BodyInteractable ]====================
    public class BodyInteractable : InteractableObject
    {
        //====================[ Properties & Fields ]====================
        public Player Revivee { get; set; }
        public Player reviver;

        private const float REVIVE_HOLD_TIME = 2f;

        //====================[ Interactivity Methods ]====================
        public void OnRevive(GamePlayerOwner owner)
        {
            if (Revivee is null || owner.Player is null) return;

            if (owner.Player.CurrentState is not IdleStateClass)
            {
                VFX_UI.Text(Color.yellow, "You can't revive a player while moving");
                return;
            }

            VFX_UI.ObjectivePanel(Color.cyan, VFX_UI.Position.Default, "Reviving {0:F1}", REVIVE_HOLD_TIME);

            var handler = new ReviveCompleteHandler
            {
                owner = owner,
                targetId = Revivee.ProfileId,
                reviverId = owner.Player.ProfileId
            };

            owner.Player.CurrentManagedState.Plant(true, false, REVIVE_HOLD_TIME, handler.Complete);
            FikaBridge.SendTeamHelpPacket(Revivee.ProfileId, owner.Player.ProfileId);
        }

        // True while a MedPickerInteractable is open over this body.
        // TickBodyInteractableColliderState reads this to avoid re-enabling the collider mid-pick.
        public bool HasActivePicker { get; set; }

        public ActionsReturnClass GetActions(GamePlayerOwner owner)
        {
            var actions = new ActionsReturnClass();

            if (Revivee == null || owner?.Player == null) return actions;

            // A downed player cannot revive or heal others — they can't perform any interaction.
            if (RMSession.IsPlayerCritical(owner.Player.ProfileId)) return actions;

            bool playerCritical = RMSession.IsPlayerCritical(Revivee.ProfileId);

            if (playerCritical)
            {
                // Patient is downed — Revive only, never Heal at the same time.
                bool hasDefib = Utils.HasDefib(owner.Player);
                actions.Actions.Add(new ActionsTypesClass
                {
                    Action   = () => OnRevive(owner),
                    Name     = "Revive",
                    Disabled = !hasDefib
                });
            }
            else
            {
                // Patient is alive but injured — show "Heal" opener if healer has anything useful.
                bool hasMeds = false;
                foreach (var _ in TeamMedical.GetUsableMeds(owner.Player, Revivee)) { hasMeds = true; break; }

                if (hasMeds)
                {
                    actions.Actions.Add(new ActionsTypesClass
                    {
                        Action   = () => OpenMedPicker(owner),
                        Name     = "Heal",
                        Disabled = false
                    });
                }
            }

            return actions;
        }

        private void OpenMedPicker(GamePlayerOwner owner)
        {
            try
            {
                HasActivePicker = true;

                // Disable this collider so the next F-press hits the picker, not this object.
                var col = GetComponent<BoxCollider>();
                if (col != null) col.enabled = false;

                // Spawn picker as a sibling on the same bone at the same position/scale.
                var anchor = transform.parent != null ? transform.parent : transform;
                var pickerGo = InteractableBuilder<MedPickerInteractable>.Build(
                    "Med Picker", Vector3.zero, transform.localScale, anchor, null, RevivalModSettings.TESTING.Value);

                var picker = pickerGo?.GetComponent<MedPickerInteractable>();
                if (picker == null)
                {
                    Plugin.LogSource.LogError("[BodyInteractable] OpenMedPicker: picker component missing after Build");
                    RestoreFromPicker();
                    return;
                }

                picker.Init(owner.Player, Revivee, this);
                pickerGo.layer = LayerMask.NameToLayer("Interactive");
                var pickerCol = pickerGo.GetComponent<BoxCollider>();
                if (pickerCol != null) pickerCol.enabled = true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[BodyInteractable] OpenMedPicker error: {ex.Message}");
                RestoreFromPicker();
            }
        }

        // Called by MedPickerInteractable when it closes (item picked or cancelled).
        // TickBodyInteractableColliderState will re-enable this collider on the next frame
        // if the patient is still injured/critical.
        public void RestoreFromPicker()
        {
            HasActivePicker = false;
        }

        //====================[ ReviveCompleteHandler ]====================
        internal class ReviveCompleteHandler
        {
            //====================[ Fields ]====================
            public GamePlayerOwner owner;
            public string targetId;
            public string reviverId;

            //====================[ Callbacks ]====================
            public void Complete(bool result)
            {
                VFX_UI.HideObjectivePanel();

                if (result)
                {
                    // Authorize revive start to transition server state and prevent duplicates
                    if (!RevivalAuthority.TryAuthorizeReviveStart(targetId, reviverId, "team", out var denyReason))
                    {
                        FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                        VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied" : denyReason);
                        return;
                    }

                    // Consume the reviver's defib if configured
                    if (!RevivalModSettings.TESTING.Value && RevivalModSettings.CONSUME_DEFIB_ON_TEAMMATE_REVIVE.Value)
                    {
                        var defib = Utils.GetDefib(owner.Player);
                        if (defib != null) Utils.ConsumeDefibItem(owner.Player, defib);
                    }

                    FikaBridge.SendTeamReviveStartPacket(targetId, reviverId);

                    // Ensure local fake items exist so the sender can resolve IDs when the ProceedPacket arrives
                    var downedPlayer = Utils.GetPlayerById(targetId);
                    if (downedPlayer != null)
                    {
                        MedicalAnimations.EnsureFakeItemsForRemotePlayer(downedPlayer);
                    }

                    Plugin.LogSource.LogInfo($"Revive hold completed for {targetId}");
                }
                else
                {
                    FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                    VFX_UI.Text(Color.yellow, "Revive cancelled!");
                }
            }
        }
    }
}