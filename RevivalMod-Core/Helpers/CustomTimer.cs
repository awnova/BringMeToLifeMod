//====================[ Imports ]====================
using System;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading;
using System.Runtime.ConstrainedExecution;

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

    //====================[ Custom Timer Class ]====================
    // Provides a simple countdown or stopwatch timer with a custom in-game UI
    // Uses the existing LocationTransitTimerPanel from EFT’s GameUI as the display surface
    public class CustomTimer
    {
        //====================[ Fields ]====================
        // Timing data
        private DateTime targetEndTime;          // UTC time when countdown ends
        private DateTime startTime;              // UTC time when timer started
        private bool isCountdown;                // Determines timer type
        private string timerName;                // Internal name for logging/debugging
        private TimerPosition timerPosition;     // Screen anchor position for display

        // UI references
        private TextMeshProUGUI titleText;       // Reference to timer text component

        //====================[ Properties ]====================
        public bool IsRunning { get; set; }      // Indicates if timer is currently active
        public string TimerLabel { get; private set; } = "Timer"; // Display name shown on screen

        //====================[ Constructors ]====================
        // Basic constructor initializes a clean instance with no running state
        public CustomTimer() { }

        //====================[ Timer Control ]====================
        // Starts a countdown for the specified duration
        // Displays time decreasing toward zero
        public void StartCountdown(float durationInSeconds, string name = "Countdown", TimerPosition position = TimerPosition.BottomCenter)
        {
            isCountdown = true;
            IsRunning = true;
            timerName = name;
            TimerLabel = name;
            timerPosition = position;

            // Set target time
            startTime = DateTime.UtcNow;
            targetEndTime = startTime.AddSeconds(durationInSeconds);

            // Create and show the timer UI
            CreateTimerUI();
        }

        // Starts a stopwatch timer (elapsed time counting upward)
        public void StartStopwatch(string name = "Stopwatch", TimerPosition position = TimerPosition.TopCenter)
        {
            isCountdown = false;
            IsRunning = true;
            timerName = name;
            timerPosition = position;

            
            startTime = DateTime.UtcNow;

            // Create and show the timer UI
            CreateTimerUI();
        }

        // Stops the timer and hides its UI
        public void StopTimer()
        {
            IsRunning = false;
            MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel.Close();
        }

        //====================[ Update Loop ]====================
        // called each frame to refresh the timer text
        public void Update()
        {
            if (!IsRunning || titleText == null)
                return;

            TimeSpan timeSpan = GetTimeSpan();

            // Check and Stop automatically if countdown finishes
            if (isCountdown && timeSpan.TotalSeconds <= 0)
            {
                titleText.text = "00:00.000";
                StopTimer();
                return;
            }

            // Update display text in real time maybe...
            titleText.text = $"{TimerLabel}: {GetFormattedTime()}";
        }

        //====================[ Time Utilities ]====================
        // Returns the raw TimeSpan value (remaining for countdown, elapsed for stopwatch)
        public TimeSpan GetTimeSpan()
        {
            if (!isCountdown)
                return DateTime.UtcNow - startTime;

            TimeSpan remaining = targetEndTime - DateTime.UtcNow;
            return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
        }

        // Returns the formatted time as MM:SS:MMM
        public string GetFormattedTime()
        {
            TimeSpan timeSpan = GetTimeSpan();
            if (isCountdown && timeSpan.TotalSeconds <= 0)
                return "00:00:000";

            return $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}:{timeSpan.Milliseconds:000}";
        }

        //====================[ UI Handling ]====================
        // Creates or reuses the LocationTransitTimerPanel to display timer data
        private void CreateTimerUI()
        {
            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel;
                ui.Display(); // Make panel visible if hidden

                // Configure the layout and appearance
                RectTransform panel = ui.transform.GetChild(0) as RectTransform;
                if (panel != null)
                {
                    panel.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    panel.sizeDelta = new Vector2(318, 51);
                    panel.GetComponent<Image>().color = Color.red; // Temporary visual indicator
                }

                // Initialize text component for live updates
                titleText = ui.GetComponentInChildren<TextMeshProUGUI>();
                titleText.text = $"{TimerLabel}: {GetFormattedTime()}";
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
