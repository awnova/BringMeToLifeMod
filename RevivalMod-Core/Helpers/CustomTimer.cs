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
    //====================[ Screen Positions ]====================
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

    //====================[ Color Modes ]====================
    public enum TimerColorMode
    {
        RedToGrey,   // bleedout / danger cooling off
        RedToGreen,  // healing / being saved
        RedToBlack,  // about to die
        StaticBlue,  // "Being revived" / help incoming
        Static       // no color change
    }

    //====================[ Timer Display ]====================
    // Renders a countdown or stopwatch on screen using GameUI.LocationTransitTimerPanel.
    public class CustomTimer
    {
        //-------------[ State ]-------------
        private bool isCountdown;
        private string timerName;
        private TimerPosition timerPosition;
        private TimerColorMode colorMode;
        private DateTime startTime;
        private DateTime targetEndTime;
        private float totalDuration;

        //-------------[ UI Refs ]-------------
        private TextMeshProUGUI titleText;
        private Image panelImage;

        //-------------[ Public Flags ]-------------
        public bool IsRunning { get; set; }
        public string TimerLabel { get; private set; } = "Timer";

        //====================[ Start Countdown ]====================
        public void StartCountdown(
            float seconds,
            string label = "Countdown",
            TimerPosition position = TimerPosition.BottomCenter,
            TimerColorMode mode = TimerColorMode.RedToGrey)
        {
            isCountdown = true;
            IsRunning = true;
            timerName = label;
            TimerLabel = label;
            timerPosition = position;
            colorMode = mode;
            totalDuration = seconds;

            startTime = DateTime.UtcNow;
            targetEndTime = startTime.AddSeconds(seconds);

            CreateTimerUI();
        }

        //====================[ Start Stopwatch ]====================
        public void StartStopwatch(
            string label = "Stopwatch",
            TimerPosition position = TimerPosition.TopCenter)
        {
            isCountdown = false;
            IsRunning = true;
            timerName = label;
            TimerLabel = label;
            timerPosition = position;
            colorMode = TimerColorMode.Static;

            startTime = DateTime.UtcNow;

            CreateTimerUI();
        }

        //====================[ Stop / Hide ]====================
        public void StopTimer()
        {
            IsRunning = false;

            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance?.LocationTransitTimerPanel;
                if (ui != null && ui.isActiveAndEnabled) ui.Close();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"StopTimer(): {ex.Message}");
            }
        }

        //====================[ Per-Frame Update ]====================
        public void Update()
        {
            if (!IsRunning || titleText == null) return;

            var span = GetTimeSpan();

            // auto-stop when countdown hits zero
            if (isCountdown && span.TotalSeconds <= 0)
            {
                titleText.text = $"{TimerLabel.ToUpperInvariant()}: 00:00.000";
                StopTimer();
                return;
            }

            titleText.text = $"{TimerLabel.ToUpperInvariant()}: {GetFormattedTime()}";
            UpdatePanelColor(span);
        }

        //====================[ Time Math ]====================
        // Remaining time (countdown) or elapsed time (stopwatch)
        public TimeSpan GetTimeSpan()
        {
            if (!isCountdown) return DateTime.UtcNow - startTime;

            var remaining = targetEndTime - DateTime.UtcNow;
            return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
        }

        // "MM:SS:MMM"
        public string GetFormattedTime()
        {
            var span = GetTimeSpan();
            if (isCountdown && span.TotalSeconds <= 0) return "00:00:000";

            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}:{span.Milliseconds:000}";
        }

        //====================[ UI Build ]====================
        private void CreateTimerUI()
        {
            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel;
                ui.Display();

                // panel
                var panel = ui.transform.GetChild(0) as RectTransform;
                if (panel != null)
                {
                    panel.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    panel.sizeDelta = new Vector2(318, 51);

                    panelImage = panel.GetComponent<Image>();
                    panelImage.color = colorMode == TimerColorMode.StaticBlue
                        ? Color.blue
                        : (isCountdown ? Color.red : Color.grey);

                    ApplyAnchorPosition(panel);
                }

                // text
                titleText = ui.GetComponentInChildren<TextMeshProUGUI>();
                titleText.text = $"{TimerLabel.ToUpperInvariant()}: {GetFormattedTime()}";
                titleText.autoSizeTextContainer = true;
                titleText.fontSize = 22;

                Plugin.LogSource.LogDebug($"Timer UI '{timerName}' at {timerPosition}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"CreateTimerUI(): {ex.Message}\n{ex.StackTrace}");
            }
        }

        //====================[ Color Logic ]====================
        private void UpdatePanelColor(TimeSpan span)
        {
            if (!isCountdown || panelImage == null || totalDuration <= 0) return;

            float remaining = (float)span.TotalSeconds;
            float t = 1f - (remaining / totalDuration); // 0=start, 1=end

            switch (colorMode)
            {
                case TimerColorMode.RedToGrey:
                    panelImage.color = Color.Lerp(Color.red, Color.grey, t);
                    break;
                case TimerColorMode.RedToGreen:
                    panelImage.color = Color.Lerp(Color.red, Color.green, t);
                    break;
                case TimerColorMode.RedToBlack:
                    panelImage.color = Color.Lerp(Color.red, Color.black, t);
                    break;
                case TimerColorMode.StaticBlue:
                    panelImage.color = Color.blue;
                    break;
                case TimerColorMode.Static:
                    break;
            }
        }

        //====================[ Positioning ]====================
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

        //====================[ Static Message Display ]====================
        // Show a message panel (no ticking time)
        public void ShowStaticMessage(
            string message,
            TimerPosition position = TimerPosition.BottomCenter,
            Color backgroundColor = default)
        {
            IsRunning = true;
            timerName = message;
            TimerLabel = message;
            timerPosition = position;
            colorMode = TimerColorMode.Static;
            isCountdown = false;

            if (backgroundColor == default) backgroundColor = Color.grey;

            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance.LocationTransitTimerPanel;
                ui.Display();

                var panel = ui.transform.GetChild(0) as RectTransform;
                if (panel != null)
                {
                    panel.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    panel.sizeDelta = new Vector2(318, 51);

                    panelImage = panel.GetComponent<Image>();
                    panelImage.color = backgroundColor;

                    ApplyAnchorPosition(panel);
                }

                titleText = ui.GetComponentInChildren<TextMeshProUGUI>();
                titleText.text = message;
                titleText.autoSizeTextContainer = true;
                titleText.fontSize = 22;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"ShowStaticMessage(): {ex.Message}");
            }
        }

        //====================[ Update Static Text ]====================
        public void UpdateStaticMessage(string message)
        {
            if (titleText != null) titleText.text = message;
        }
    }
}
