//====================[ Imports ]====================
using UnityEngine;
using KeepMeAlive.Components;

namespace KeepMeAlive.Helpers
{
    //====================[ RevivePolicy ]====================
    internal static class RevivePolicy
    {
        //====================[ Policy Queries ]====================
        public static bool IsEnabled(ReviveSource source)
        {
            return source switch
            {
                ReviveSource.Self => KeepMeAliveSettings.SELF_REVIVAL_ENABLED.Value,
                ReviveSource.Team => KeepMeAliveSettings.TEAM_REVIVE_ENABLED.Value,
                _ => true
            };
        }

        public static float GetHoldDuration(ReviveSource source)
        {
            float configured = source switch
            {
                ReviveSource.Self => KeepMeAliveSettings.SELF_REVIVE_HOLD_TIME.Value,
                ReviveSource.Team => KeepMeAliveSettings.TEAM_REVIVE_HOLD_TIME.Value,
                _ => 2f
            };
            return Mathf.Max(0.1f, configured);
        }

        public static float GetProgressDuration(ReviveSource source)
        {
            float configured = source switch
            {
                ReviveSource.Self => KeepMeAliveSettings.SELF_REVIVE_ANIMATION_DURATION.Value,
                ReviveSource.Team => KeepMeAliveSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value,
                _ => 3f
            };
            return Mathf.Max(3f, configured);
        }

        public static bool ShouldConsumeReviveItem(ReviveSource source)
        {
            return source switch
            {
                ReviveSource.Self => KeepMeAliveSettings.CONSUME_REVIVE_ITEM_ON_SELF_REVIVE.Value,
                ReviveSource.Team => KeepMeAliveSettings.CONSUME_REVIVE_ITEM_ON_TEAMMATE_REVIVE.Value,
                _ => false
            };
        }

        //====================[ Authority Routing ]====================
        public static bool UseResilientAuthority(ReviveSource source)
        {
            return source == ReviveSource.Team;
        }
    }
}
