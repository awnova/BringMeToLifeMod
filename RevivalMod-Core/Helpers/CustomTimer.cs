//====================[ Imports ]====================
using System;
using System.Threading;
using System.Runtime.ConstrainedExecution;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

//====================[ CustomTimer ]====================
namespace RevivalMod.Helpers
{
    //====================[ Enums ]====================
    // Defines possible on-screen anchor points for the timer display
    public enum TimerPosition
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    // Defines color transition behavior for the timer
    public enum TimerColorMode
    {
        RedToGrey,      // Default: transitions from red to grey (danger diminishing)
        RedToGreen,     // Revival: transitions from red to green (healing in progress)
        RedToBlack,     // Critical: transitions from red to black (death approaching)
        StaticBlue,     // Static blue color (for "Being revived")
        Static          // No color transition
    }

    //====================[ Custom Timer Class ]====================
    // Provides a simple countdown or stopwatch timer with a custom in-game UI
    // Uses EFT’s LocationTransitTimerPanel as the display surface
    public class CustomTimer
    {
        //====================[ Fields ]====================
        // Timing data
        private bool isCountdown;                // Determines timer type (countdown or stopwatch)
        private string timerName;                // Internal name for logging/debugging
        private TimerPosition timerPosition;     // Screen anchor position for display
        private TimerColorMode colorMode;        // Color transition mode
        private DateTime startTime;              // UTC time when timer started
        private DateTime targetEndTime;          // UTC time when countdown ends
        private float totalDuration;             // Total duration in seconds (for color interpolation)

        // UI references
        private TextMeshProUGUI titleText;       // Timer text component
        private Image panelImage;                // Panel background for color transitions

        //====================[ Properties ]====================
        public bool IsRunning { get; set; }      // Indicates if timer is currently active
        public string TimerLabel { get; private set; } = "Timer"; // Display name shown on screen

        //====================[ Constructors ]====================
        // Initializes a clean timer instance
        public CustomTimer() { }

        //====================[ Timer Control ]====================
        // Starts a countdown for the specified duration
        public void StartCountdown(float durationInSeconds, string name = "Countdown", TimerPosition position = TimerPosition.BottomCenter, TimerColorMode colorTransition = TimerColorMode.RedToGrey)
        {
            isCountdown = true;
            IsRunning = true;
            timerName = name;
            TimerLabel = name;
            timerPosition = position;
            colorMode = colorTransition;
            totalDuration = durationInSeconds;

            startTime = DateTime.UtcNow;
            targetEndTime = startTime.AddSeconds(durationInSeconds);

            CreateTimerUI();
        }

        // Starts a stopwatch timer (elapsed time counting upward)
        public void StartStopwatch(string name = "Stopwatch", TimerPosition position = TimerPosition.TopCenter)
        {
            isCountdown = false;
            IsRunning = true;
            timerName = name;
            TimerLabel = name;
            timerPosition = position;
            colorMode = TimerColorMode.Static;

            startTime = DateTime.UtcNow;

            CreateTimerUI();
        }

        // Stops the timer and hides its UI safely
        public void StopTimer()
        {
            IsRunning = false;

            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance?.LocationTransitTimerPanel;
                if (ui != null && ui.isActiveAndEnabled)
                    ui.Close();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error stopping timer UI: {ex.Message}");
            }
        }

        //====================[ Update Loop ]====================
        // Called each frame to refresh the timer text and visual color transitions
        public void Update()
        {
            if (!IsRunning || titleText == null)
                return;

            TimeSpan timeSpan = GetTimeSpan();

            // Stop automatically when countdown finishes
            if (isCountdown && timeSpan.TotalSeconds <= 0)
            {
                titleText.text = $"{TimerLabel.ToUpperInvariant()}: 00:00.000";
                StopTimer();
                return;
            }

            // Update label text
            titleText.text = $"{TimerLabel.ToUpperInvariant()}: {GetFormattedTime()}";

            // Update background color
            UpdatePanelColor(timeSpan);
        }

        //====================[ Time Utilities ]====================
        // Returns the raw TimeSpan (remaining for countdown, elapsed for stopwatch)
        public TimeSpan GetTimeSpan()
        {
            if (!isCountdown)
                return DateTime.UtcNow - startTime;

            TimeSpan remaining = targetEndTime - DateTime.UtcNow;
            return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
        }

        // Returns formatted time as MM:SS:MMM
        public string GetFormattedTime()
        {
            TimeSpan timeSpan = GetTimeSpan();
            if (isCountdown && timeSpan.TotalSeconds <= 0)
                return "00:00:000";

            return $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}:{timeSpan.Milliseconds:000}";
        }

        //====================[ UI Handling ]====================
        // Creates or reuses the LocationTransitTimerPanel for display
        private void CreateTimerUI()
        {
            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel;
                ui.Display();

                // Configure layout
                RectTransform panel = ui.transform.GetChild(0) as RectTransform;
                if (panel != null)
                {
                    panel.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    panel.sizeDelta = new Vector2(318, 51);

                    panelImage = panel.GetComponent<Image>();
                    
                    // Set initial color based on color mode
                    panelImage.color = colorMode == TimerColorMode.StaticBlue ? Color.blue : 
                                      (isCountdown ? Color.red : Color.grey);

                    ApplyAnchorPosition(panel);
                }

                // Initialize text component
                titleText = ui.GetComponentInChildren<TextMeshProUGUI>();
                titleText.text = $"{TimerLabel.ToUpperInvariant()}: {GetFormattedTime()}";
                titleText.autoSizeTextContainer = true;
                titleText.fontSize = 22;

                Plugin.LogSource.LogDebug($"Created custom timer UI for '{timerName}' at {timerPosition}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error creating custom timer UI: {ex.Message}\n{ex.StackTrace}");
            }
        }

        //====================[ Visual Utilities ]====================
        // Transitions panel color based on timer progress and color mode
        private void UpdatePanelColor(TimeSpan timeSpan)
        {
            if (!isCountdown || panelImage == null || totalDuration <= 0)
                return;

            float remainingSeconds = (float)timeSpan.TotalSeconds;
            float progress = 1f - (remainingSeconds / totalDuration); // 0=start, 1=end

            switch (colorMode)
            {
                case TimerColorMode.RedToGrey:
                    panelImage.color = Color.Lerp(Color.red, Color.grey, progress);
                    break;
                case TimerColorMode.RedToGreen:
                    panelImage.color = Color.Lerp(Color.red, Color.green, progress);
                    break;
                case TimerColorMode.RedToBlack:
                    panelImage.color = Color.Lerp(Color.red, Color.black, progress);
                    break;
                case TimerColorMode.StaticBlue:
                    // Keep static blue color
                    panelImage.color = Color.blue;
                    break;
                case TimerColorMode.Static:
                    // No color change
                    break;
            }
        }

        // Positions the panel based on the selected TimerPosition enum
        private void ApplyAnchorPosition(RectTransform panel)
        {
            switch (timerPosition)
            {
                case TimerPosition.TopLeft:
                    panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
                    panel.anchoredPosition = new Vector2(100, -50);
                    break;
                case TimerPosition.TopCenter:
                    panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
                    panel.anchoredPosition = new Vector2(0, -50);
                    break;
                case TimerPosition.TopRight:
                    panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
                    panel.anchoredPosition = new Vector2(-100, -50);
                    break;
                case TimerPosition.MiddleLeft:
                    panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
                    panel.anchoredPosition = new Vector2(100, 0);
                    break;
                case TimerPosition.MiddleCenter:
                    panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                    panel.anchoredPosition = new Vector2(0, 0);
                    break;
                case TimerPosition.MiddleRight:
                    panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
                    panel.anchoredPosition = new Vector2(-100, 0);
                    break;
                case TimerPosition.BottomLeft:
                    panel.anchorMin = panel.anchorMax = new Vector2(0f, 0f);
                    panel.anchoredPosition = new Vector2(100, 50);
                    break;
                case TimerPosition.BottomCenter:
                    panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
                    panel.anchoredPosition = new Vector2(0, 50);
                    break;
                case TimerPosition.BottomRight:
                    panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
                    panel.anchoredPosition = new Vector2(-100, 50);
                    break;
            }
        }
    }
}
