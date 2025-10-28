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
                owner.ShowObjectivesPanel("Reviving {0:F1}", REVIVE_HOLD_TIME);

                // Try to color the objectives panel green for teammate revival
                try
                {
                    var objectivesPanel = MonoBehaviourSingleton<GameUI>.Instance?.TimerPanel;
                    if (objectivesPanel != null)
                    {
                        RectTransform panel = objectivesPanel.transform.GetChild(0) as RectTransform;
                        if (panel != null)
                        {
                            var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
                            if (panelImage != null)
                            {
                                panelImage.color = UnityEngine.Color.green;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogDebug($"Could not color objectives panel: {ex.Message}");
                }

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
            // Check entire inventory for defib
            bool hasDefib = RevivalFeatures.HasDefib(owner.Player);        
            bool playerCritState = RMSession.GetCriticalPlayers().Contains(Revivee.ProfileId);
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