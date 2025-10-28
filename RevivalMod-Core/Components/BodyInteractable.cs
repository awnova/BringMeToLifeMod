using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using System;
using UnityEngine;
using RevivalMod.Helpers;
using RevivalMod.Features;
using RevivalMod.Fika;

namespace RevivalMod.Components
{
    public class BodyInteractable : InteractableObject
    {
        public Player Revivee { get; set; }
        public Player reviver;

        public void OnRevive(GamePlayerOwner owner)
        {
            // Reviver holds for 2 seconds to initiate revival
            const float REVIVE_HOLD_TIME = 2f;

            if (Revivee is null)
            {
                Plugin.LogSource.LogError("Revivee is null, cannot perform revival.");
                return;
            }

            if (owner.Player is null)
            {
                Plugin.LogSource.LogError("Interactor is null, cannot perform revival.");
                return;
            }

            if (owner.Player.CurrentState is IdleStateClass)
            {
                // Show hold prompt using ShowObjectivesPanel (TimerPanel) for the 2-second hold
                owner.ShowObjectivesPanel("Reviving {0:F1}", REVIVE_HOLD_TIME);

                // Color the objectives panel blue for teammate revival
                VFX_UI.ColorObjectivesPanelBlue();

                // Start the countdown, and trigger the ActionCompleteHandler when it's done
                MovementState currentManagedState = owner.Player.CurrentManagedState;

                ReviveCompleteHandler actionCompleteHandler = new()
                {
                    owner = owner,
                    targetId = Revivee.ProfileId
                };
                
                Action<bool> action = new(actionCompleteHandler.Complete);

                currentManagedState.Plant(true, false, REVIVE_HOLD_TIME, action);

                FikaBridge.SendReviveStartedPacket(Revivee.ProfileId, owner.Player.ProfileId);
            }
            else
            {
                owner.DisplayPreloaderUiNotification("You can't revive a player while moving");
            }
        }

        public ActionsReturnClass GetActions(GamePlayerOwner owner)
        {
            ActionsReturnClass actionsReturnClass = new();

            // Null checks to prevent errors
            if (Revivee == null)
            {
                Plugin.LogSource.LogError("GetActions: Revivee is null");
                return actionsReturnClass;
            }

            if (owner?.Player == null)
            {
                Plugin.LogSource.LogError("GetActions: Owner or Owner.Player is null");
                return actionsReturnClass;
            }

            // Check entire inventory for defib
            bool hasDefib = Utils.HasDefib(owner.Player);
            
            // Use RMSession.IsPlayerCritical as single source of truth
            bool playerCritState = RMSession.IsPlayerCritical(Revivee.ProfileId);
            bool reviveButtonEnabled = playerCritState && hasDefib;

            Plugin.LogSource.LogDebug($"Revivee {Revivee.ProfileId} critical state is {playerCritState}, has defib: {hasDefib}");
           
            actionsReturnClass.Actions.Add(new ActionsTypesClass()
            {
                Action = () => OnRevive(owner),
                Name = "Revive",
                Disabled = !reviveButtonEnabled
            });

            return actionsReturnClass;
        }

        internal class ReviveCompleteHandler
        {
            public GamePlayerOwner owner;
            public string targetId;

            public void Complete(bool result)
            {
                owner.CloseObjectivesPanel();
                
                if (result)
                {
                    // Send packet to trigger animation on revivee
                    bool success = RevivalFeatures.PerformTeammateRevival(targetId, owner.Player);
                    
                    if (success)
                    {
                        Plugin.LogSource.LogInfo($"Revive hold completed, teammate now playing animation");
                    }
                }
                else
                {
                    FikaBridge.SendReviveCanceledPacket(targetId, owner.Player.ProfileId);

                    Plugin.LogSource.LogInfo($"Revive cancelled!");
                }
            }
        }
    }
}