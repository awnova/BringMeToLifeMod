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
    // Spawned by BodyInteractable when the healer clicks "Heal".
    // Shows one action per med item the healer can apply to the patient, plus "Cancel".
    // Destroys itself on any selection and restores BodyInteractable's collider.
    public class MedPickerInteractable : InteractableObject
    {
        //====================[ Properties ]====================
        public BodyInteractable OwnerBody { get; private set; }
        public Player Healer              { get; private set; }
        public Player Patient             { get; private set; }

        //====================[ Init ]====================
        public void Init(Player healer, Player patient, BodyInteractable ownerBody)
        {
            Healer    = healer;
            Patient   = patient;
            OwnerBody = ownerBody;
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

                int addedMeds = 0;
                foreach (var item in TeamMedical.GetUsableMeds(Healer, Patient))
                {
                    MedsItemClass captured = item;
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

                actions.Actions.Add(new ActionsTypesClass
                {
                    Name     = "Cancel",
                    Disabled = false,
                    Action   = Close
                });
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[MedPickerInteractable] GetActions error: {ex.Message}");
            }
            return actions;
        }

        //====================[ Private Helpers ]====================
        private void OnPickItem(MedsItemClass item)
        {
            // Close picker first so BodyInteractable collider comes back immediately.
            Close();
            TeamMedical.BeginHeal(Healer, Patient, item);
        }

        private void Close()
        {
            OwnerBody?.RestoreFromPicker();
            Destroy(gameObject);
        }
    }
}
