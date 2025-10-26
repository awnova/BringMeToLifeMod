using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using System;
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

            // Use a hardcoded 2 second hold time for teammate revives per design decision
            float reviveTime = RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value;

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
                // Stop forcing empty hands on the revivee to allow any potential animations
                if (RevivalFeatures._playerList.ContainsKey(Revivee.ProfileId))
                {
                    RevivalFeatures._playerList[Revivee.ProfileId].IsPlayingRevivalAnimation = true;
                }
                
                // Play the CMS animation on the reviver and sync its playback to the UI revive countdown (so both finish together).
                // Ignore the return value so a broken animation won't block the actual revive.
                _ = MedicalAnimations.PlaySurgicalAnimationForDuration(owner.Player, MedicalAnimations.SurgicalItemType.CMS, reviveTime);

                owner.ShowObjectivesPanel("Reviving {0:F1}", reviveTime);

                // Start the countdown, and trigger the ActionCompleteHandler when it's done
                MovementState currentManagedState = owner.Player.CurrentManagedState;

                ReviveCompleteHandler actionCompleteHandler = new()
                {
                    owner = owner,
                    targetId = Revivee.ProfileId
                };
                
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
            bool playerCritState = RMSession.GetCriticalPlayers().TryGetValue(Revivee.ProfileId, out _);
            bool reviveButtonEnabled = playerCritState && hasDefib;

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

        internal class ReviveCompleteHandler
        {
            public GamePlayerOwner owner;
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
                    // Resume forcing empty hands on the revivee if revival was cancelled
                    if (RevivalFeatures._playerList.ContainsKey(targetId))
                    {
                        RevivalFeatures._playerList[targetId].IsPlayingRevivalAnimation = false;
                    }
                    
                    FikaBridge.SendReviveCanceledPacket(targetId, owner.Player.ProfileId);

                    Plugin.LogSource.LogInfo($"Revive not completed !");
                }
            }
        }
    }
}