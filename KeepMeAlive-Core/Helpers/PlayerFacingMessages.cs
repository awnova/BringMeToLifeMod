//====================[ Imports ]====================
using EFT;
using KeepMeAlive.Components;
using UnityEngine;

namespace KeepMeAlive.Helpers
{
    //====================[ PlayerFacingMessages ]====================
    // Centralized catalog for player-visible text.
    internal static class PlayerFacingMessages
    {
        //====================[ Interactions ]====================
        internal static class Interaction
        {
            public const string TeamReviveDisabled = "Team revive is disabled";
            public const string CannotReviveWhileMoving = "You can't revive a player while moving";
            public const string RevivingObjective = "Reviving {0:F1}";
            public const string ReviveAction = "Revive";
            public const string ReviveNoLongerPossible = "Revive no longer possible";
            public const string ReviveCancelled = "Revive cancelled!";

            public const string MedicBleeds = "Medic Bleeds";
            public const string MedicBreaks = "Medic Breaks";
            public const string MedicHealth = "Medic Health";
            public const string MedicComfort = "Medic Comfort";
            public const string MedicNutrition = "Medic Nutrition";

            public const string MedPickerName = "Med Picker";
            public const string CancelAction = "Cancel";
        }

        //====================[ Team Heal ]====================
        internal static class TeamHeal
        {
            public const string CannotHealWhileMoving = "You can't heal while moving";
            public const string HealingObjective = "Healing teammate {0:F1}";
            public const string HealingCancelled = "Healing cancelled!";
            public const string PatientUnavailable = "Patient is no longer available";
            public const string HealingTeammate = "Healing teammate...";
            public const string YouWereHealed = "You were healed!";

            public static string StartedBy(string healerDisplay, string patientDisplay) =>
                $"{healerDisplay} is healing {patientDisplay}";

            public static string CancelledBy(string healerDisplay) =>
                $"{healerDisplay} cancelled healing";
        }

        //====================[ Revive Flow ]====================
        internal static class Revive
        {
            public const string NoReviveItemFound = "No revive item found! Unable to revive!";
            public const string HoldObjective = "Hold {0:F1}";
            public const string RevivingProgress = "REVIVING";
            public const string SelfReviveCanceled = "Self-revive canceled";
            public const string MissingItemCanceled = "Revive Item missing. Self-revive canceled.";
        }

        //====================[ Revive Complete ]====================
        internal static class ReviveComplete
        {
            public const string LocalSelf = "Revive Item used successfully! You are temporarily invulnerable.";
            public const string LocalTeam = "Revived by teammate! You are temporarily invulnerable.";

            public static string ObserverBySource(string playerDisplay, ReviveSource source)
            {
                return source == ReviveSource.Self
                    ? $"{playerDisplay} self-revived"
                    : $"{playerDisplay} was revived";
            }
        }

        //====================[ Revive Denials ]====================
        internal static class ReviveDenied
        {
            public const string AuthorizationFailed = "Revive authorization failed";
            public const string TeamFallback = "Revive denied";
            public const string SelfFallback = "Revive denied by server";
            public const string Cooldown = "Player on cooldown";
        }

        //====================[ Network Revive ]====================
        internal static class NetworkRevive
        {
            public static string TeamHelpedBy(string reviverName, string reviveeName) =>
                $"{reviverName} is helping {reviveeName}";

            public static string RevivingYou(string reviverName) =>
                $"{reviverName} is reviving you...";

            public static string TeamCancelledBy(string reviverName) =>
                $"{reviverName} cancelled revival";

            public static string SelfReviving(string display) =>
                $"{display} is self-reviving";

            public static string TeamReviving(string reviverName, string reviveeName) =>
                $"{reviverName} is reviving {reviveeName}";

            public static string RevivePrompt(KeyCode key) =>
                $"Revive! [{key}]";
        }

        //====================[ Downed UI ]====================
        internal static class Downed
        {
            public const string DownedBanner = "DOWNED";
            public const string BleedingOut = "BLEEDING OUT";
            public const string ReviverTimedOut = "Reviver disconnected or timed out.";
        }

        //====================[ Post-Revive ]====================
        internal static class PostRevive
        {
            public const string CooldownEnded = "Revival cooldown ended - you can now be revived";
            public const string InvulnerableObjective = "Invulnerable {0:F1}";

            public static string InvulnerabilityEnded(float cooldownSeconds) =>
                $"Invulnerability ended. Revival cooldown: {cooldownSeconds:F0}s";
        }

        //====================[ Death ]====================
        internal static class Death
        {
            public const string HeadshotCritical = "Headshot – critical";
            public const string HeadshotKilled = "Headshot – killed instantly";
            public const string YouDied = "You died";
        }

        //====================[ Ghost Mode ]====================
        internal static class GhostMode
        {
            public static string State(bool entered) =>
                $"GhostMode: {(entered ? "Entered (F7)" : "Exited (F8)")}";
        }
    }
}
