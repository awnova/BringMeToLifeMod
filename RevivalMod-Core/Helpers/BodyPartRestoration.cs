//====================[ Imports ]====================
using EFT;
using EFT.HealthSystem;
using RevivalMod.Helpers;
using RevivalMod.Fika;
using System;
using System.Reflection;

//====================[ BodyPartRestoration ]====================
namespace RevivalMod.Helpers
{
    internal static class BodyPartRestoration
    {
        //====================[ Safety Checks & Entry Point ]====================
        // Called after revive. Heals any body part that was completely blacked.
        public static void RestoreDestroyedBodyParts(Player player, bool sendNetworkPacket = true)
        {
            //--------------[ Basic sanity / settings gate ]--------------
            if (player == null)
            {
                Plugin.LogSource.LogError("Restore failed: player is null.");
                return;
            }

            if (!RevivalModSettings.RESTORE_DESTROYED_BODY_PARTS.Value)
            {
                Plugin.LogSource.LogDebug("Restore skipped: feature disabled in settings.");
                return;
            }

            try
            {
                var hc = player.ActiveHealthController;
                if (hc == null)
                {
                    Plugin.LogSource.LogError("Restore failed: ActiveHealthController is null.");
                    return;
                }

                Plugin.LogSource.LogInfo("Restoring destroyed body parts…");

                // Walk every body part (head, chest, etc.) and repair only the ones that are "destroyed"
                foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
                {
                    // skip 'Common' (that's global HP pool / abstract)
                    if (part == EBodyPart.Common) continue;

                    var state = hc.Dictionary_0[part];
                    Plugin.LogSource.LogDebug($"{part} at {hc.GetBodyPartHealth(part).Current} hp");

                    if (!state.IsDestroyed) continue; // only touch blacked parts

                    RestoreOneBodyPart(hc, part, state);
                }

                Plugin.LogSource.LogInfo("Body part restoration complete.");

                // Send network packet to sync with other clients (only if this is the local player's restoration)
                if (sendNetworkPacket && player.IsYourPlayer)
                {
                    FikaBridge.SendHealthRestoredPacket(player.ProfileId);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Restore error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        //====================[ Fix One Body Part ]====================
        // Takes a single blacked limb/torso/etc, marks it no longer destroyed, gives it HP, clears debuffs.
        private static void RestoreOneBodyPart(
            ActiveHealthController hc,
            EBodyPart part,
            GClass2814<ActiveHealthController.GClass2813>.BodyPartState state)
        {
            try
            {
                // mark limb as no longer destroyed
                state.IsDestroyed = false;

                // figure out how much HP that limb should come back with
                float pct = GetRestorePercentFor(part);
                float newHp = state.Health.Maximum * pct;

                // set its health (current / max / dmgTaken=0)
                state.Health = new HealthValue(newHp, state.Health.Maximum, 0f);

                // tell the health system "this got medically fixed"
                hc.method_43(part, EDamageType.Medicine); // internal heal event
                hc.method_35(part);                       // refresh status
                hc.RemoveNegativeEffects(part);           // clear fractures/bleed/etc for that part

                // broadcast to anything listening (UI, armor visuals, etc.)
                FireBodyPartRestoredEvent(hc, part, state.Health.CurrentAndMaximum);

                Plugin.LogSource.LogDebug(
                    $"Restored {part} → {pct * 100f:0.#}% ({newHp}/{state.Health.Maximum})."
                );
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Restore {part} error: {ex.Message}");
            }
        }

        //====================[ How Much Health To Give Back ]====================
        // Reads per-body-part % from settings, like "Head gets 35%" or "Arms get 60%".
        private static float GetRestorePercentFor(EBodyPart part)
        {
            return part switch
            {
                EBodyPart.Head =>
                    RevivalModSettings.RESTORE_HEAD_PERCENTAGE.Value / 100f,

                EBodyPart.Chest =>
                    RevivalModSettings.RESTORE_CHEST_PERCENTAGE.Value / 100f,

                EBodyPart.Stomach =>
                    RevivalModSettings.RESTORE_STOMACH_PERCENTAGE.Value / 100f,

                EBodyPart.LeftArm or EBodyPart.RightArm =>
                    RevivalModSettings.RESTORE_ARMS_PERCENTAGE.Value / 100f,

                EBodyPart.LeftLeg or EBodyPart.RightLeg =>
                    RevivalModSettings.RESTORE_LEGS_PERCENTAGE.Value / 100f,

                _ => 0.5f // fallback 50% if we meet something weird
            };
        }

        //====================[ Notify Game Systems / UI ]====================
        // Manually fires the game's "BodyPartRestoredEvent" so other systems react.
        private static void FireBodyPartRestoredEvent(
            ActiveHealthController hc,
            EBodyPart part,
            ValueStruct healthValue)
        {
            try
            {
                // BodyPartRestoredEvent is private so we grab it with reflection
                var field = typeof(ActiveHealthController)
                    .GetField("BodyPartRestoredEvent", BindingFlags.Instance | BindingFlags.NonPublic);

                if (field == null)
                {
                    Plugin.LogSource.LogWarning("BodyPartRestoredEvent not found (reflection).");
                    return;
                }

                if (field.GetValue(hc) is not MulticastDelegate del)
                {
                    Plugin.LogSource.LogDebug("BodyPartRestoredEvent has no subscribers.");
                    return;
                }

                // call every listener
                foreach (var handler in del.GetInvocationList())
                    handler.DynamicInvoke(part, healthValue);

                Plugin.LogSource.LogDebug($"BodyPartRestoredEvent fired for {part}.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Event dispatch error: {ex.Message}");
            }
        }
    }
}
