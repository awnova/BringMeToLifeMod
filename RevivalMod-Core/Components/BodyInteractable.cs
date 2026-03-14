//====================[ Imports ]====================
using EFT;
using EFT.Interactive;
using EFT.UI;
using System;
using System.Threading;
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
                // Patient is alive but injured — always show all 4 categories.
                // Grey out any where the healer has no applicable meds OR where the patient
                // has no active condition for that category, so the healer can see at a glance
                // what is both available and actually needed.
                foreach (MedCategory cat in System.Enum.GetValues(typeof(MedCategory)))
                {
                    MedCategory captured = cat;
                    bool hasMeds     = TeamMedical.HealerHasMedForCategory(owner.Player, captured);
                    bool patientNeeds = TeamMedical.PatientNeedsCategory(Revivee, captured);
                    actions.Actions.Add(new ActionsTypesClass
                    {
                        Action   = () => OpenFilteredMedPicker(owner, captured),
                        Name     = CategoryLabel(captured),
                        Disabled = !hasMeds || !patientNeeds
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
                // Safety guard: if target transitioned to downed, never open the medical picker.
                if (Revivee == null || RMSession.IsPlayerCritical(Revivee.ProfileId))
                {
                    return;
                }

                HasActivePicker = true;

                // Disable this collider so the next F-press hits the picker, not this object.
                foreach (var col in GetComponents<Collider>())
                {
                    col.enabled = false;
                }
                var anchor = transform.parent != null ? transform.parent : transform;
                var pickerGo = InteractableBuilder<MedPickerInteractable>.Build(
                    "Med Picker", transform.localPosition, transform.localScale, anchor, null, RevivalModSettings.FREE_TEAM_HEALING.Value);

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
                // Delay collider activation by one frame so the F keypress that
                // triggered OpenFilteredMedPicker does not immediately bleed into
                // the picker and execute its first action (was "Cancel").
                Plugin.StaticCoroutineRunner.StartCoroutine(EnablePickerColliderNextFrame(pickerGo));
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

        private System.Collections.IEnumerator EnablePickerColliderNextFrame(GameObject pickerGo)
        {
            yield return null; // skip the frame that triggered OpenFilteredMedPicker
            if (pickerGo == null) yield break;
            foreach (var col in pickerGo.GetComponents<Collider>())
            {
                col.enabled = true;
            }
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
            private static int _globalAuthAttemptId;
            private int _activeAuthAttemptId;

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
                    int attemptId = Interlocked.Increment(ref _globalAuthAttemptId);
                    _activeAuthAttemptId = attemptId;
                    Plugin.StaticCoroutineRunner.StartCoroutine(TeamReviveAuthCoroutine(attemptId));
                }
                else
                {
                    FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                    VFX_UI.Text(Color.yellow, "Revive cancelled!");
                }
            }

            private System.Collections.IEnumerator TeamReviveAuthCoroutine(int attemptId)
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

                if (task.IsFaulted)
                {
                    allowed = false;
                    denyReason = "Revive authorization failed";
                    Plugin.LogSource.LogWarning($"[ReviveAuth] Authorization task fault for target={targetId} reviver={reviverId}: {task.Exception?.GetBaseException().Message}");
                }

                if (_activeAuthAttemptId != attemptId)
                {
                      if (allowed) RevivalAuthority.NotifyBeginCritical(targetId); // Rollback backend
                      Plugin.LogSource.LogInfo($"[ReviveAuth] Ignoring stale team auth result attempt={attemptId} active={_activeAuthAttemptId} target={targetId}");
                      yield break;
                  }

                  // Re-validate: revivee state may have changed while auth was pending.
                  // Allow Reviving as well as BleedingOut — the server may have already committed
                  // to Reviving via a fast resync packet that arrived during the HTTP call, or the
                  // server's TryStartRevive already set the state on its side.
                  var reviveeState = RMSession.GetPlayerState(targetId);
                  if (reviveeState.State != RMState.BleedingOut && reviveeState.State != RMState.Reviving)
                  {
                      if (allowed) RevivalAuthority.NotifyBeginCritical(targetId); // Rollback backend
                    yield break;
                }

                if (allowed)
                {
                    if (!RevivalModSettings.NO_DEFIB_REQUIRED.Value && RevivalModSettings.CONSUME_DEFIB_ON_TEAMMATE_REVIVE.Value)
                    {
                        var defib = Utils.GetDefib(owner.Player);
                        if (defib != null && !Utils.TryApplyItemLikeTeamHeal(owner.Player, defib, "TeamReviveDefib"))
                        {
                            Plugin.LogSource.LogWarning($"[TeamReviveDefib] ApplyItem did not consume defib for {owner.Player?.ProfileId}");
                        }
                    }

                    FikaBridge.SendTeamReviveStartPacket(targetId, reviverId);

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