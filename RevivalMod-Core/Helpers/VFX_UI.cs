using System;
using EFT;
using EFT.UI;
using EFT.Communications;
using UnityEngine;
using UnityEngine.UI;

namespace RevivalMod.Helpers
{
    /// <summary>
    /// Handles visual effects and UI manipulation for the revival mod, including panel coloring
    /// </summary>
    internal static class VFX_UI
    {
        #region UI Panel Effects

        /// <summary>
        /// Colors the objectives panel (timer panel) with the specified color
        /// </summary>
        /// <param name="color">The color to apply to the panel background</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool ColorObjectivesPanel(Color color)
        {
            try
            {
                var objectivesPanel = MonoBehaviourSingleton<GameUI>.Instance?.TimerPanel;
                if (objectivesPanel == null)
                {
                    Plugin.LogSource.LogDebug("TimerPanel not available");
                    return false;
                }

                RectTransform panel = objectivesPanel.transform.GetChild(0) as RectTransform;
                if (panel == null)
                {
                    Plugin.LogSource.LogDebug("Could not get panel RectTransform");
                    return false;
                }

                var panelImage = panel.GetComponent<Image>();
                if (panelImage == null)
                {
                    Plugin.LogSource.LogDebug("Could not get panel Image component");
                    return false;
                }

                panelImage.color = color;
                Plugin.LogSource.LogDebug($"Colored objectives panel: {color}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"Could not color objectives panel: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Colors the objectives panel blue (commonly used for revival operations)
        /// </summary>
        public static bool ColorObjectivesPanelBlue()
        {
            return ColorObjectivesPanel(Color.blue);
        }

        #endregion

        #region Notification Helpers

        /// <summary>
        /// Shows a critical/error notification (red)
        /// </summary>
        public static void ShowCriticalNotification(string message, ENotificationDurationType duration = ENotificationDurationType.Long)
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                duration,
                ENotificationIconType.Alert,
                Color.red);
        }

        /// <summary>
        /// Shows a success notification (green)
        /// </summary>
        public static void ShowSuccessNotification(string message, ENotificationDurationType duration = ENotificationDurationType.Long)
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                duration,
                ENotificationIconType.Default,
                Color.green);
        }

        /// <summary>
        /// Shows a warning notification (yellow)
        /// </summary>
        public static void ShowWarningNotification(string message, ENotificationDurationType duration = ENotificationDurationType.Long)
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                duration,
                ENotificationIconType.Alert,
                Color.yellow);
        }

        /// <summary>
        /// Shows an info notification (cyan)
        /// </summary>
        public static void ShowInfoNotification(string message, ENotificationDurationType duration = ENotificationDurationType.Default)
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                duration,
                ENotificationIconType.Default,
                Color.cyan);
        }

        /// <summary>
        /// Shows a white/neutral notification
        /// </summary>
        public static void ShowNotification(string message, ENotificationDurationType duration = ENotificationDurationType.Default)
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                duration,
                ENotificationIconType.Default,
                Color.white);
        }

        /// <summary>
        /// Shows a friend/teammate notification (green, friend icon)
        /// </summary>
        public static void ShowTeammateNotification(string message, ENotificationDurationType duration = ENotificationDurationType.Long)
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                duration,
                ENotificationIconType.Friend,
                Color.green);
        }

        /// <summary>
        /// Displays critical state notification with available options
        /// </summary>
        public static void ShowCriticalStateNotification(Player player)
        {
            try
            {
                // Build notification message
                string message = "CRITICAL CONDITION!\n";

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                {
                    message += $"Hold {RevivalModSettings.SELF_REVIVAL_KEY.Value} for 2s to use defibrillator ({(int)RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value}s animation)\n";
                }

                message += $"Press {RevivalModSettings.GIVE_UP_KEY.Value} to give up\n";
                message += $"Or wait for a teammate to revive you ({(int)RevivalModSettings.CRITICAL_STATE_TIME.Value} seconds)";

                ShowCriticalNotification(message);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error displaying critical state UI: {ex.Message}");
            }
        }

        #endregion

        #region ObjectivesPanel Helpers

        /// <summary>
        /// Shows the objectives panel with a colored background
        /// </summary>
        public static void ShowObjectivesPanelWithColor(GamePlayerOwner owner, string message, float duration, Color color)
        {
            owner.ShowObjectivesPanel(message, duration);
            ColorObjectivesPanel(color);
        }

        /// <summary>
        /// Shows the objectives panel with blue background (default for revival)
        /// </summary>
        public static void ShowObjectivesPanelBlue(GamePlayerOwner owner, string message, float duration)
        {
            ShowObjectivesPanelWithColor(owner, message, duration, Color.blue);
        }

        #endregion
    }
}
