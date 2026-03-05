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

        private MedPickerInteractable _activeMedPicker;

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
                // Patient is alive but injured — add one action per non-empty med category.
                foreach (MedCategory cat in System.Enum.GetValues(typeof(MedCategory)))
                {
                    if (!TeamMedical.HealerHasMedForCategory(owner.Player, cat)) continue;

                    MedCategory captured = cat;
                    actions.Actions.Add(new ActionsTypesClass
                    {
                        Action   = () => OpenFilteredMedPicker(owner, captured),
                        Name     = CategoryLabel(captured),
                        Disabled = false
                    });
                }
            }

            return actions;
        }

        private static string CategoryLabel(MedCategory cat) => cat switch
        {
            MedCategory.Bleeds  => "Medic Bleeds",
            MedCategory.Breaks  => "Medic Breaks",
            MedCategory.Health  => "Medic Health",
            MedCategory.Comfort => "Medic Comfort",
            _                   => cat.ToString()
        };

        // Opens a filtered med picker for the given category.
        // Called directly from GetActions action lambdas.
        public void OpenFilteredMedPicker(GamePlayerOwner owner, MedCategory category)
        {
            try
            {
                HasActivePicker = true;

                // Disable this collider so the next F-press hits the picker, not this object.
                var col = GetComponent<BoxCollider>();
                if (col != null) col.enabled = false;
                var anchor = transform.parent != null ? transform.parent : transform;
                var pickerGo = InteractableBuilder<MedPickerInteractable>.Build(
                    "Med Picker", Vector3.zero, transform.localScale, anchor, null, RevivalModSettings.TESTING.Value);

                var picker = pickerGo?.GetComponent<MedPickerInteractable>();
                if (picker == null)
                {
                    Plugin.LogSource.LogError("[BodyInteractable] OpenFilteredMedPicker: picker component missing after Build");
                    RestoreFromPicker();
                    return;
                }

                picker.Init(owner.Player, Revivee, this, category);
                _activeMedPicker = picker;
                pickerGo.layer = LayerMask.NameToLayer("Interactive");
                var pickerCol = pickerGo.GetComponent<BoxCollider>();
                if (pickerCol != null) pickerCol.enabled = true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[BodyInteractable] OpenFilteredMedPicker error: {ex.Message}");
                RestoreFromPicker();
            }
        }

        // Called by MedPickerInteractable when it closes (item picked or cancelled).
        // TickBodyInteractableColliderState will re-enable this collider on the next frame
        // if the patient is still injured/critical.
        public void RestoreFromPicker()
        {
            _activeMedPicker = null;
            HasActivePicker = false;
        }

        // Called when the patient enters the downed state while the picker may still be open.
        // Prevents HasActivePicker from being permanently stuck true after the patient is revived.
        public void ForceClosePicker()
        {
            if (_activeMedPicker != null)
            {
                try { Destroy(_activeMedPicker.gameObject); } catch { }
                _activeMedPicker = null;
            }
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
                    // #12: Validate that the revivee is still revivable. The 2-second hold is enough time
                    // for them to die, self-revive, or be claimed by a different reviver.
                    var reviveeState = RMSession.GetPlayerState(targetId);
                    if (reviveeState.State != RMState.BleedingOut)
                    {
                        FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                        VFX_UI.Text(Color.yellow, "Revive no longer possible");
                        return;
                    }

                    // #3: Run auth off the Unity main thread so a slow HTTP call never freezes the frame.
                    Plugin.StaticCoroutineRunner.StartCoroutine(TeamReviveAuthCoroutine());
                }
                else
                {
                    FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                    VFX_UI.Text(Color.yellow, "Revive cancelled!");
                }
            }

            private System.Collections.IEnumerator TeamReviveAuthCoroutine()
            {
                bool allowed = true;
                string denyReason = string.Empty;

                // Run blocking HTTP call on background thread.
                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    allowed    = RevivalAuthority.TryAuthorizeReviveStart(targetId, reviverId, "team", out var reason);
                    denyReason = reason ?? string.Empty;
                });

                while (!task.IsCompleted) yield return null;

                // Re-validate: revivee state may have changed while auth was pending.
                var reviveeState = RMSession.GetPlayerState(targetId);
                if (reviveeState.State != RMState.BleedingOut)
                {
                    FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                    yield break;
                }

                if (allowed)
                {
                    if (!RevivalModSettings.TESTING.Value && RevivalModSettings.CONSUME_DEFIB_ON_TEAMMATE_REVIVE.Value)
                    {
                        var defib = Utils.GetDefib(owner.Player);
                        if (defib != null) Utils.ConsumeDefibItem(owner.Player, defib);
                    }

                    FikaBridge.SendTeamReviveStartPacket(targetId, reviverId);

                    var downedPlayer = Utils.GetPlayerById(targetId);
                    if (downedPlayer != null) MedicalAnimations.EnsureFakeItemsForRemotePlayer(downedPlayer);

                    Plugin.LogSource.LogInfo($"Revive hold completed for {targetId}");
                }
                else
                {
                    FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                    VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied" : denyReason);
                }
            }
        }
    }
}