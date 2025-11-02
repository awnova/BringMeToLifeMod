//====================[ Imports ]====================
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
                // Show hold timer UI
                VFX_UI.ObjectivePanel(Color.cyan, VFX_UI.Position.Default, "Reviving {0:F1}", REVIVE_HOLD_TIME);

                // Start the countdown
                MovementState currentManagedState = owner.Player.CurrentManagedState;

                ReviveCompleteHandler actionCompleteHandler = new()
                {
                    owner = owner,
                    targetId = Revivee.ProfileId,
                    reviverId = owner.Player.ProfileId
                };

                Action<bool> action = new(actionCompleteHandler.Complete);
                currentManagedState.Plant(true, false, REVIVE_HOLD_TIME, action);

                // Send TeamHelpPacket - announce to revivee that someone is helping
                FikaBridge.SendTeamHelpPacket(Revivee.ProfileId, owner.Player.ProfileId);
            }
            else
            {
                VFX_UI.Text(Color.yellow, "You can't revive a player while moving");
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
            public string reviverId;

            public void Complete(bool result)
            {
                VFX_UI.HideObjectivePanel();

                if (result)
                {
                    // Hold completed - send TeamReviveStartPacket to begin revival animation
                    FikaBridge.SendTeamReviveStartPacket(targetId, reviverId);
                    Plugin.LogSource.LogInfo($"Revive hold completed, sent TeamReviveStartPacket for {targetId}");
                }
                else
                {
                    // Hold cancelled - send TeamCancelPacket
                    FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                    VFX_UI.Text(Color.yellow, "Revive cancelled!");
                    Plugin.LogSource.LogInfo("Revive cancelled!");
                }
            }
        }
    }
}
