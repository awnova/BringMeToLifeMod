//====================[ Imports ]====================
using System;
using System.Collections;
using System.Threading;
using EFT;
using EFT.Interactive;
using EFT.UI;
using KeepMeAlive.Features;
using KeepMeAlive.Fika;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Components
{
    //====================[ BodyInteractable ]====================
    public class BodyInteractable : InteractableObject
    {
        //====================[ Fields ]====================
        public Player Revivee { get; set; }
        public bool HasActivePicker { get; set; }
        
        private BoxCollider _collider;
        private MedPickerInteractable _activeMedPicker;
        private float _nextCheckTime;
        
        // Configuration
        private const float INTERACTION_MAX_DISTANCE_SQ = 9f; // 3 meters squared
        private const float UPDATE_INTERVAL = 1.0f; // Slower update baseline
        
        public static float ReviveHoldTime => RevivePolicy.GetHoldDuration(ReviveSource.Team);

        //====================[ Unity Lifecycle ]====================
        private void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            if (_collider != null)
            {
                _collider.enabled = false;
            }
            gameObject.layer = LayerMask.NameToLayer("Interactive");
        }

        private void Update()
        {
            if (Revivee == null || _collider == null) return;

            // Safety: bots should never expose revival/team-heal interactables.
            if (Revivee.IsAI || Revivee.AIData?.IsAI == true)
            {
                try { Destroy(gameObject); } catch { }
                return;
            }
            
            // Throttle checks
            if (Time.time < _nextCheckTime) return;
            _nextCheckTime = Time.time + UPDATE_INTERVAL;

            // 1. State Gating
            // If player state isn't critical or injured, we shouldn't be interactable at all.
            bool isCritical = RMSession.IsPlayerCritical(Revivee.ProfileId);
            var state = RMSession.GetPlayerState(Revivee.ProfileId);
            bool isRevived = state?.State == RMState.Revived;
            
            bool isInjured = false;
            if (!isCritical && !isRevived && Revivee.HealthController != null)
            {
                foreach (MedCategory cat in Enum.GetValues(typeof(MedCategory)))
                {
                    if (TeamMedical.PatientNeedsCategory(Revivee, cat))
                    {
                        isInjured = true;
                        break;
                    }
                }
            }
            
            bool shouldEnable = isCritical || isRevived || isInjured;

            if (!shouldEnable || HasActivePicker)
            {
                if (_collider.enabled) _collider.enabled = false;
                return;
            }

            // 2. Distance Gating
            Transform camTransform = Camera.main?.transform;
            if (camTransform == null) return;

            Vector3 camPos = camTransform.position;
            Vector3 centerPos = _collider.transform.position; // Faster than bounds.center
            float distSq = (centerPos - camPos).sqrMagnitude;

            bool withinDistance = distSq <= INTERACTION_MAX_DISTANCE_SQ;
            if (_collider.enabled != withinDistance)
            {
                _collider.enabled = withinDistance;
            }
        }

        //====================[ Setup / Attachment ]====================
        public static BodyInteractable AttachToPlayer(Player player)
        {
            if (player == null) return null; 
            
            // Prevent multiple attachments if PlayerId is set multiple times
            if (player.gameObject.GetComponentInChildren<BodyInteractable>() != null) return null;

            // We use a coroutine to wait for bones, and also to wait until after Fika attaches
            Plugin.StaticCoroutineRunner.StartCoroutine(WaitForBonesAndBuild(player));
            return null; // Return null synchronously, we cache it dynamically later anyway
        }

        private static IEnumerator WaitForBonesAndBuild(Player player)
        {
            // Wait for bones to be ready and Profile to be fully assigned
            while (player == null || player.PlayerBones == null || player.PlayerBones.RootJoint == null || player.Profile == null || string.IsNullOrEmpty(player.ProfileId))
            {
                yield return null;
            }
            
            // Wait slightly after bones are ready (allow Fika UI to spawn)
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (player == null) yield break;

            // Final safeguards against wrong owner/type and late AI initialization races.
            if (player.IsYourPlayer || player.IsAI || player.AIData?.IsAI == true) yield break;

            var go = new GameObject("Body Interactable");
            // Set parent to the root player object to have predictable local coordinates (Y is up, Z is forward)
            go.transform.SetParent(player.gameObject.transform, false);
            
            // Position the transform centrally (Y=0.9 is half of 1.8m average height)
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.layer = LayerMask.NameToLayer("Interactive");

            var bi = go.AddComponent<BodyInteractable>();
            bi.Revivee = player;
            Features.BodyInteractableRuntime.Register(player.ProfileId, bi);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = false;
            
            // Pre-defined collider size covering head to toe
            box.center = Vector3.zero;
            box.size = new Vector3(0.8f, 1.8f, 0.8f);

            go.SetActive(true);
        }

        //====================[ Logic & Interaction ]====================

        public void OnRevive(GamePlayerOwner owner)
        {
            if (Revivee is null || owner?.Player is null) return;

            if (!RevivePolicy.IsEnabled(ReviveSource.Team))
            {
                VFX_UI.Text(Color.yellow, "Team revive is disabled");
                return;
            }

            if (owner.Player.CurrentState is not IdleStateClass)
            {
                VFX_UI.Text(Color.yellow, "You can't revive a player while moving");
                return;
            }

            VFX_UI.ObjectivePanel(Color.cyan, VFX_UI.Position.Default, "Reviving {0:F1}", ReviveHoldTime);

            var handler = new ReviveCompleteHandler
            {
                owner = owner,
                targetId = Revivee.ProfileId,
                reviverId = owner.Player.ProfileId
            };

            owner.Player.CurrentManagedState.Plant(true, false, ReviveHoldTime, handler.Complete);
            FikaBridge.SendTeamHelpPacket(Revivee.ProfileId, owner.Player.ProfileId);
        }

        public ActionsReturnClass GetActions(GamePlayerOwner owner)
        {
            var actions = new ActionsReturnClass();

            if (Revivee == null || owner?.Player == null) return actions;
            if (Revivee.IsAI || Revivee.AIData?.IsAI == true) return actions;
            if (RMSession.IsPlayerCritical(owner.Player.ProfileId)) return actions;

            bool playerCritical = RMSession.IsPlayerCritical(Revivee.ProfileId);

            if (playerCritical)
            {
                if (!RevivePolicy.IsEnabled(ReviveSource.Team)) return actions;

                bool canRevive = KeepMeAliveSettings.NO_REVIVE_ITEM_REQUIRED.Value || Utils.HasReviveItem(owner.Player);
                actions.Actions.Add(new ActionsTypesClass
                {
                    Action = () => OnRevive(owner),
                    Name = "Revive",
                    Disabled = !canRevive
                });
            }
            else
            {
                foreach (MedCategory cat in Enum.GetValues(typeof(MedCategory)))
                {
                    MedCategory captured = cat;
                    bool patientNeeds = TeamMedical.PatientNeedsCategory(Revivee, captured);
                    
                    if (!patientNeeds) continue; // Only show category if the patient needs healing for it

                    bool hasMeds = TeamMedical.HealerHasMedForCategory(owner.Player, captured);
                    actions.Actions.Add(new ActionsTypesClass
                    {
                        Action = () => OpenFilteredMedPicker(owner, captured),
                        Name = CategoryLabel(captured),
                        Disabled = !hasMeds
                    });
                }
            }

            return actions;
        }

        public void OpenFilteredMedPicker(GamePlayerOwner owner, MedCategory category)
        {
            try
            {
                if (Revivee == null || RMSession.IsPlayerCritical(Revivee.ProfileId)) return;

                HasActivePicker = true;
                if (_collider != null) _collider.enabled = false;

                // Spawn MedPicker with same bounds as the full body collider
                var pickerGo = InteractableBuilder<MedPickerInteractable>.Build(
                    "Med Picker", 
                    Vector3.zero, 
                    new Vector3(0.8f, 1.8f, 0.8f), 
                    transform, 
                    null, 
                    KeepMeAliveSettings.FREE_TEAM_HEALING.Value
                );

                var picker = pickerGo?.GetComponent<MedPickerInteractable>();
                if (picker == null)
                {
                    Plugin.LogSource.LogError("[BodyInteractable] OpenFilteredMedPicker: picker missing");
                    RestoreFromPicker();
                    return;
                }

                picker.Init(owner.Player, Revivee, this, category);
                _activeMedPicker = picker;
                pickerGo.layer = LayerMask.NameToLayer("Interactive");
                
                // Allow it to exist now
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[BodyInteractable] OpenFilteredMedPicker error: {ex.Message}");
                RestoreFromPicker();
            }
        }

        public void RestoreFromPicker()
        {
            _activeMedPicker = null;
            HasActivePicker = false;
            // The Update loop will re-enable our collider on its next tick if conditions are met.
        }

        public void ForceClosePicker()
        {
            if (_activeMedPicker != null)
            {
                try { UnityEngine.Object.Destroy(_activeMedPicker.gameObject); } catch { }
                _activeMedPicker = null;
            }
            HasActivePicker = false;
        }

        private static string CategoryLabel(MedCategory cat)
        {
            return cat switch
            {
                MedCategory.Bleeds => "Medic Bleeds",
                MedCategory.Breaks => "Medic Breaks",
                MedCategory.Health => "Medic Health",
                MedCategory.Comfort => "Medic Comfort",
                MedCategory.Nutrition => "Medic Nutrition",
                _ => cat.ToString()
            };
        }

        //====================[ ReviveCompleteHandler ]====================
        internal class ReviveCompleteHandler
        {
            private static int _globalAuthAttemptId;
            private int _activeAuthAttemptId;

            public GamePlayerOwner owner;
            public string targetId;
            public string reviverId;

            public void Complete(bool result)
            {
                VFX_UI.HideObjectivePanel();

                if (result)
                {
                    var reviveeState = RMSession.GetPlayerState(targetId);
                    if (reviveeState.State != RMState.BleedingOut)
                    {
                        FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                        VFX_UI.Text(Color.yellow, "Revive no longer possible");
                        return;
                    }

                    int attemptId = Interlocked.Increment(ref _globalAuthAttemptId);
                    _activeAuthAttemptId = attemptId;
                    Plugin.StaticCoroutineRunner.StartCoroutine(
                        RevivalController.TeamReviveAuthStartCoroutine(
                            owner.Player,
                            targetId,
                            reviverId,
                            () => _activeAuthAttemptId == attemptId));
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