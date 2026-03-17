//====================[ Imports ]====================
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using System;
using UnityEngine;
using KeepMeAlive.Features;
using KeepMeAlive.Helpers;

namespace KeepMeAlive.Components
{
    //====================[ MedPickerInteractable ]====================
    // Spawned by BodyInteractable after the healer selects a category.
    // Shows one action per med item in that category the healer can apply to the patient, plus "Cancel".
    // Destroys itself on any selection and restores BodyInteractable's collider.
    public class MedPickerInteractable : InteractableObject
    {
        //====================[ Properties ]====================
        public BodyInteractable OwnerBody { get; private set; }
        public Player Healer              { get; private set; }
        public Player Patient             { get; private set; }

        //====================[ Fields ]====================
        private MedCategory? _category;
        private Collider _collider;
        private float _nextCheckTime;
        
        // Configuration
        private const float INTERACTION_MAX_DISTANCE_SQ = 9f; // 3 meters squared
        private const float UPDATE_INTERVAL = 1.0f;

        //====================[ Unity Lifecycle ]====================
        private void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_collider != null)
            {
                _collider.enabled = false;
            }
        }

        private void Update()
        {
            if (Healer == null || Patient == null || _collider == null) return;
            
            // Validate states
            bool patientCritical = RMSession.IsPlayerCritical(Patient.ProfileId);
            if (patientCritical)
            {
                Close(); // Can't use med picker on a critical patient, close it.
                return;
            }

            // Throttle checks
            if (Time.time < _nextCheckTime) return;
            _nextCheckTime = Time.time + UPDATE_INTERVAL;

            // Distance gating
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

        //====================[ Init ]====================
        public void Init(Player healer, Player patient, BodyInteractable ownerBody, MedCategory? category = null)
        {
            Healer    = healer;
            Patient   = patient;
            OwnerBody = ownerBody;
            _category = category;
        }

        //====================[ GetActions ]====================
        public ActionsReturnClass GetActions(GamePlayerOwner owner)
        {
            var actions = new ActionsReturnClass();
            try
            {
                if (Healer == null || Patient == null)
                {
                    Close();
                    return actions;
                }

                actions.Actions.Add(new ActionsTypesClass
                {
                    Name     = "Cancel",
                    Disabled = false,
                    Action   = Close
                });

                int addedMeds = 0;
                foreach (var item in TeamMedical.GetUsableMedsByCategory(Healer, Patient, _category))
                {
                    Item captured = item;
                    actions.Actions.Add(new ActionsTypesClass
                    {
                        Name     = captured.ShortName.Localized(),
                        Disabled = false,
                        Action   = () => OnPickItem(captured)
                    });
                    addedMeds++;
                }

                // If nothing is usable any more (e.g. healer used all meds between opener and picker),
                // silently close so the player doesn't see an empty wheel.
                if (addedMeds == 0)
                {
                    Close();
                    return actions;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[MedPickerInteractable] GetActions error: {ex.Message}");
            }
            return actions;
        }

        //====================[ Private Helpers ]====================
        private void OnPickItem(Item item)
        {
            // Do NOT close the picker yet. If the user cancels the animation, this picker should remain open.
            // _collider.enabled = false; // Could disable collider while planting if we want to prevent double-interaction, but leaving it as-is is safer for hold interactions.
            TeamMedical.BeginHeal(Healer, Patient, item, (success) =>
            {
                if (success)
                {
                    // Heal successful (applied), return to category screen.
                    Close();
                }
                // If it was cancelled (!success), we do nothing. The picker survives.
            });
        }

        private void Close()
        {
            OwnerBody?.RestoreFromPicker();
            Destroy(gameObject);
        }
    }
}
