using System;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading;
using System.Runtime.ConstrainedExecution;

namespace RevivalMod.Helpers
{
    /// <summary>
    /// Positions for the custom timer UI
    /// </summary>
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

    /// <summary>
    /// Custom timer implementation with a fully custom UI
    /// </summary>
    public class CustomTimer
    {
        // Timer data
        private DateTime targetEndTime;
        private DateTime startTime;
        private bool isCountdown;
        public bool IsRunning { get; set; }
        private string timerName;
        private TimerPosition timerPosition;

        // UI components
        private TextMeshProUGUI titleText;

        public CustomTimer()
        {
        }

        /// <summary>
        /// Start a countdown timer with specified duration
        /// </summary>
        public void StartCountdown(float durationInSeconds, string name = "Countdown", TimerPosition position = TimerPosition.BottomCenter)
        {
            isCountdown = true;
            IsRunning = true;
            timerName = name;
            timerPosition = position;

            // Set target time
            startTime = DateTime.UtcNow;
            targetEndTime = startTime.AddSeconds(durationInSeconds);

            // Create and show the timer UI
            CreateTimerUI();
        }

        /// <summary>
        /// Start a stopwatch to measure elapsed time
        /// </summary>
        public void StartStopwatch(string name = "Stopwatch", TimerPosition position = TimerPosition.TopCenter)
        {
            isCountdown = false;
            IsRunning = true;
            timerName = name;
            timerPosition = position;

            // Set start time
            startTime = DateTime.UtcNow;

            // Create and show the timer UI
            CreateTimerUI();
        }

        /// <summary>
        /// Update the timer (call this every frame)
        /// </summary>
        public void Update()
        {
            if (!IsRunning || titleText == null)
                return;

            TimeSpan timeSpan = GetTimeSpan();

            // Check if countdown complete
            if (isCountdown && timeSpan.TotalSeconds <= 0)
            {
                titleText.text = "00:00.000";

                // Auto-stop if countdown finished
                StopTimer();
                return;
            }

            // Update the display
            titleText.text = "Critical State: " + GetFormattedTime();
        }

        /// <summary>
        /// Stop the timer
        /// </summary>
        public void StopTimer()
        {
            IsRunning = false;
            MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel.Close();
        }

        /// <summary>
        /// Get current timer value as TimeSpan
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            if (!isCountdown) 
                return DateTime.UtcNow - startTime;
            
            TimeSpan remaining = targetEndTime - DateTime.UtcNow;

            return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;

        }

        /// <summary>
        /// Get the current timer value as formatted string (MM:SS)
        /// </summary>
        public string GetFormattedTime()
        {
            TimeSpan timeSpan = GetTimeSpan();

            if (isCountdown && timeSpan.TotalSeconds <= 0)
                return "00:00:000";

            return $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}:{timeSpan.Milliseconds:000}";
        }

        /// <summary>
        /// Create a custom timer UI and add it to the main canvas
        /// </summary>
        private void CreateTimerUI()
        {
            try
            {
                MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel.Display();

                RectTransform panel = MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel.transform.GetChild(0) as RectTransform;

                if (panel is not null)
                {
                    panel.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    panel.sizeDelta = new Vector2(318, 51);
                    panel.GetComponent<Image>().color = Color.red;
                }

                titleText = MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel.GetComponentInChildren<TextMeshProUGUI>();

                titleText.text = "Critical State: " + GetFormattedTime();
                titleText.autoSizeTextContainer = true;
                titleText.fontSize = 22;

                Plugin.LogSource.LogDebug($"Created custom timer UI for {timerName} at position {timerPosition}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error creating custom timer UI: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}