using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using EFT;
using EFT.InventoryLogic;
using Comfort.Common;

namespace RevivalMod.Helpers
{
    //=============================================[ MedicalAnimations ]====================================================
    // Handles playing surgical/medical animations for the player without needing any real items.
    // - Spawns a temp fake item just for the animation
    // - Uses the item’s UseTime if available, else falls back to defaults
    // - Lets us change animation speed on the fly
    // - Cleans up automatically and restores hands at the end
    //=====================================================================================================================
    public static class MedicalAnimations
    {
        //====================[ Constants ]====================
        public const string SURVKIT_TEMPLATE_ID = "5d02797c86f774203f38e30a";
        public const string CMS_TEMPLATE_ID     = "5d02778e86f774203e7dedbe";

        private const float DEFAULT_SURVKIT_DURATION = 20f;
        private const float DEFAULT_CMS_DURATION     = 17f;
        private const float CLEANUP_BUFFER_SEC       = 0.5f;

        private static readonly Dictionary<SurgicalItemType, (string TemplateId, float DefaultSec)> Spec = new()
        {
            { SurgicalItemType.SurvKit, (SURVKIT_TEMPLATE_ID, DEFAULT_SURVKIT_DURATION) },
            { SurgicalItemType.CMS,     (CMS_TEMPLATE_ID,     DEFAULT_CMS_DURATION)     }
        };

        //====================[ Cached Reflection ]====================
        private static readonly Dictionary<Type, MethodInfo> _useTimeMultiplierCache = new();

        //====================[ Public API ]====================

        // Start a surgical animation using a temp fake item.
        // Returns true if the animation was started successfully.
        public static bool PlaySurgicalAnimation(
            Player player,
            SurgicalItemType itemType,
            float animationSpeed = 1f,
            Action onComplete = null)
        {
            if (!ValidatePlayer(player)) return false;

            var (templateId, defaultSec) = Spec[itemType];
            var item = CreateTempItem(templateId);
            if (item == null)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] Failed to create temp item ({itemType}).");
                return false;
            }

            // PlayCore returns the actual animation duration (seconds) or null on failure.
            var started = PlayCore(player, item, defaultSec, animationSpeed, onComplete, itemType.ToString());
            return started.HasValue;
        }

        // Start surgical animation and return the estimated animation duration (in seconds), or null on failure.
        public static float? PlaySurgicalAnimationWithDuration(
            Player player,
            SurgicalItemType itemType,
            float animationSpeed = 1f,
            Action onComplete = null)
        {
            if (!ValidatePlayer(player)) return null;

            var (templateId, defaultSec) = Spec[itemType];
            var item = CreateTempItem(templateId);
            if (item == null)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] Failed to create temp item ({itemType}).");
                return null;
            }

            return PlayCore(player, item, defaultSec, animationSpeed, onComplete, itemType.ToString());
        }

        // Start a surgical animation and scale its speed so the playback lasts targetDurationSeconds (in seconds).
        // Returns true if started successfully.
        public static bool PlaySurgicalAnimationForDuration(
            Player player,
            SurgicalItemType itemType,
            float targetDurationSeconds,
            Action onComplete = null)
        {
            if (!ValidatePlayer(player)) return false;

            var (templateId, defaultSec) = Spec[itemType];
            var item = CreateTempItem(templateId);
            if (item == null)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] Failed to create temp item ({itemType}).");
                return false;
            }

            // Guard target duration
            if (targetDurationSeconds <= 0f)
                targetDurationSeconds = defaultSec;

            // Calculate the speed multiplier so the base animation (defaultSec) will play in targetDurationSeconds.
            float animationSpeed = defaultSec / targetDurationSeconds;

            var started = PlayCore(player, item, defaultSec, animationSpeed, onComplete, itemType.ToString());
            return started.HasValue;
        }

        // Adjust the speed of the current animation (1.0 = normal).
        public static bool SetAnimationSpeed(Player player, float speedMultiplier)
        {
            try
            {
                if (player?.HandsAnimator == null) return false;

                var anim = player.HandsAnimator;
                var t = anim.GetType();

                if (!_useTimeMultiplierCache.TryGetValue(t, out var mi))
                {
                    mi = t.GetMethod("SetUseTimeMultiplier",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _useTimeMultiplierCache[t] = mi;
                }

                if (mi == null) return false;
                mi.Invoke(anim, new object[] { speedMultiplier });
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] Failed to set speed: {ex.Message}");
                return false;
            }
        }

        // Schedule a speed change partway through the animation.
        public static void ChangeSpeedAfterDelay(Player player, float delay, float newSpeed)
            => Plugin.StaticCoroutineRunner.StartCoroutine(ChangeSpeedRoutine(player, delay, newSpeed));

        //====================[ Core Logic ]====================

        private static float? PlayCore(
            Player player,
            Item item,
            float defaultSec,
            float animationSpeed,
            Action onComplete,
            string itemLabel)
        {
            try
            {
                if (animationSpeed <= 0f) animationSpeed = 1f;

                var baseDuration = defaultSec;

                // Play the animation (no healing logic, just visuals)
                player.Proceed(item, null, true);

                if (Math.Abs(animationSpeed - 1f) > 0.01f)
                    SetAnimationSpeed(player, animationSpeed);

                var actual = baseDuration / animationSpeed;
                var cleanup = actual + CLEANUP_BUFFER_SEC;

                Plugin.StaticCoroutineRunner.StartCoroutine(
                    CleanupAfterAnimation(player, cleanup, itemLabel, animationSpeed, onComplete));

                return actual;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] PlayCore failed: {ex.Message}");
                return null;
            }
        }

        // Note: Uses Spec default durations Not real use time.
        // Waits for animation to finish, then clears hands.
        private static IEnumerator CleanupAfterAnimation(
            Player player, float delay, string itemName, float speed, Action onComplete)
        {
            yield return new WaitForSeconds(delay);

            try
            {
                var controller = player?.HandsController;
                var name = controller?.GetType().Name ?? string.Empty;

                // If still using QuickUseItemController, force empty hands
                if (name.IndexOf("QuickUseItem", StringComparison.OrdinalIgnoreCase) >= 0)
                    player?.SetEmptyHands(null);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] Cleanup error: {ex.Message}");
            }
            finally
            {
                onComplete?.Invoke();
            }
        }

        // Handles delayed speed changes mid-animation.
        private static IEnumerator ChangeSpeedRoutine(Player player, float delay, float newSpeed)
        {
            yield return new WaitForSeconds(delay);
            SetAnimationSpeed(player, newSpeed);
        }

        private static bool ValidatePlayer(Player player)
            => player != null && player.IsYourPlayer;

        //====================[ Item Creation ]====================

        // Make a temporary item (not in inventory).
        private static Item CreateTempItem(string templateId)
        {
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory == null)
                {
                    Plugin.LogSource.LogError("[MedicalAnimations] ItemFactory not ready yet.");
                    return null;
                }

                var id = NewMongoLikeId();
                return factory.CreateItem(id, templateId, null) as Item;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] Item creation failed: {ex.Message}");
                return null;
            }
        }

        private static string NewMongoLikeId() => Guid.NewGuid().ToString("N").Substring(0, 24);

        public enum SurgicalItemType { SurvKit, CMS }
    }
}
