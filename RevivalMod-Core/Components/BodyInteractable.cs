using System;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using RevivalMod.Features;
using RevivalMod.Fika;
using RevivalMod.Helpers;

namespace RevivalMod.Components
{
    public class BodyInteractable : InteractableObject
    {
        public Player Revivee { get; set; }
        public Player reviver;

        public void OnRevive(GamePlayerOwner owner)
        {

            float reviveTime = RevivalModSettings.TEAM_REVIVAL_HOLD_DURATION.Value;

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
                owner.ShowObjectivesPanel("Reviving {0:F1}", reviveTime);

                // Start the countdown, and trigger the ActionCompleteHandler when it's done
                MovementState currentManagedState = owner.Player.CurrentManagedState;

                ReviveCompleteHandler actionCompleteHandler = new()
                {
                    owner = owner,
                    bodyInteractable = this,
                    targetId = Revivee.ProfileId
                };

                // Stop timer
                //RevivalFeatures.criticalStateMainTimer.StopTimer();

                Action<bool> action = new(actionCompleteHandler.Complete);

                currentManagedState.Plant(true, false, reviveTime, action);

                FikaBridge.SendReviveStartedPacket(Revivee.ProfileId, owner.Player.ProfileId);
            }
            else
            {
                owner.DisplayPreloaderUiNotification("You can't revive a player while moving");
            }
        }

        public ActionsReturnClass GetActions(GamePlayerOwner owner)
        {
            
            bool hasDefib = RevivalFeatures.HasDefib(owner.Player.Inventory.GetPlayerItems(EPlayerItems.Equipment));        
            bool playerCritState = RevivalFeatures._playerList[owner.Player.ProfileId].IsCritical;
            bool reviveButtonEnabled = playerCritState && (hasDefib || RevivalModSettings.TESTING.Value);

            ActionsReturnClass actionsReturnClass = new();

            Plugin.LogSource.LogDebug($"Revivee {Revivee.ProfileId} critical state is {playerCritState}");
           
            actionsReturnClass.Actions.Add(new ActionsTypesClass()
            {
                Action = () => OnRevive(owner),
                Name = "Revive",
                Disabled = !reviveButtonEnabled
            });

            return actionsReturnClass;
        }

        private class ReviveCompleteHandler
        {
            public GamePlayerOwner owner;
            public BodyInteractable bodyInteractable;
            public string targetId;

            public void Complete(bool result)
            {
                owner.CloseObjectivesPanel();
                
                if (result)
                {
                    RevivalFeatures.PerformTeammateRevival(targetId, owner.Player);

                    Plugin.LogSource.LogInfo($"Revive completed !");
                }
                else
                {
                    FikaBridge.SendReviveCanceledPacket(targetId, owner.Player.ProfileId);

                    Plugin.LogSource.LogInfo($"Revive not completed !");
                }
            }
        }
    }
}