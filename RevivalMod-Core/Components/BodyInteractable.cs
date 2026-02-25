using EFT;
using EFT.Interactive;
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

        private const float REVIVE_HOLD_TIME = 2f;

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

        public ActionsReturnClass GetActions(GamePlayerOwner owner)
        {
            var actions = new ActionsReturnClass();

            if (Revivee == null || owner?.Player == null) return actions;

            bool hasDefib = Utils.HasDefib(owner.Player);
            bool playerCritical = RMSession.IsPlayerCritical(Revivee.ProfileId);

            actions.Actions.Add(new ActionsTypesClass
            {
                Action = () => OnRevive(owner),
                Name = "Revive",
                Disabled = !(playerCritical && hasDefib)
            });

            TeamMedical.AddHealActionToBodyInteractable(this, actions, owner);

            return actions;
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
                    // Authorize with the server before telling peers to start the revive.
                    // TryAuthorizeReviveStart transitions the server's state from
                    // BleedingOut → Reviving and guards against cooldowns / duplicates.
                    if (!RevivalAuthority.TryAuthorizeReviveStart(targetId, reviverId, "team", out var denyReason))
                    {
                        FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                        VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied" : denyReason);
                        return;
                    }

                    // Consume the reviver's defib if configured
                    if (!RevivalModSettings.TESTING.Value && RevivalModSettings.CONSUME_DEFIB_ON_TEAMMATE_REVIVE.Value)
                    {
                        var defib = Utils.GetDefib(owner.Player);
                        if (defib != null) Utils.ConsumeDefibItem(owner.Player, defib);
                    }

                    FikaBridge.SendTeamReviveStartPacket(targetId, reviverId);
                    Plugin.LogSource.LogInfo($"Revive hold completed for {targetId}");
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
