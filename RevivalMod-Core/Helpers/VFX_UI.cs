//====================[ Imports ]====================
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Comfort.Common;
using EFT.UI;
using EFT.Communications;

namespace KeepMeAlive.Helpers
{
    //====================[ VFX_UI ]====================
    public static class VFX_UI
    {
        //====================[ Position Enum ]====================
        public enum Position
        {
            TopLeft, TopCenter, TopRight,
            MiddleLeft, MiddleCenter, MiddleRight,
            BottomLeft, BottomCenter, BottomRight,
            Default // Objective -> BottomCenter, Transit -> MiddleCenter
        }

        //====================[ ColorSpec ]====================
        public readonly struct ColorSpec
        {
            public readonly Color From;
            public readonly Color To;
            public readonly bool  IsGradient;
            
            private ColorSpec(Color from, Color to, bool isGradient) 
            { 
                From = from; 
                To = to; 
                IsGradient = isGradient; 
            }

            public static ColorSpec Solid(Color c)             => new ColorSpec(c, c, false);
            public static ColorSpec Gradient(Color from, Color to) => new ColorSpec(from, to, true);
            public static implicit operator ColorSpec(Color c)  => Solid(c);
        }

        // Convenience: VFX_UI.Gradient(Color.red, Color.black)
        public static ColorSpec Gradient(Color from, Color to) => ColorSpec.Gradient(from, to);

        //====================[ Transit (LocationTransitTimerPanel) ]====================
        private static RectTransform   s_transitRect;
        private static Image           s_transitImage;
        private static TextMeshProUGUI s_transitText;
        private static bool            s_transitLoopActive;
        private static ColorSpec       s_transitLoopSpec;

        //====================[ Objective (BattleUIPanelExtraction) ]====================
        private static BattleUIPanelExtraction s_extractionPanel;
        private static RectTransform           s_extractionRect;
        private static Image                   s_extractionImage;
        private static TextMeshProUGUI         s_extractionText;
        private static CustomTimer             s_extractionTimer;
        private static bool                    s_extractionLoopActive;
        private static ColorSpec               s_extractionLoopSpec;
        private static Action<TimeSpan, string, float> s_extractionOnTick;
        private static Action s_extractionOnDone;

        static VFX_UI() => EnsureDriver();

        //====================[ Quick Text ]====================
        public static void Text(Color color, string message) =>
            NotificationManagerClass.DisplayMessageNotification(
                message, ENotificationDurationType.Long, ENotificationIconType.Default, color);

        //====================[ TransitPanel ]====================
        public static CustomTimer TransitPanel(Color color, Position pos, string label, float seconds = 0f) =>
            TransitPanel((ColorSpec)color, pos, label, seconds);

        public static CustomTimer TransitPanel(ColorSpec color, Position pos, string label, float seconds = 0f)
        {
            var ui = MonoBehaviourSingleton<GameUI>.Instance?.LocationTransitTimerPanel;
            if (ui == null)
            {
                return null;
            }

            try
            {
                ui.Display();

                s_transitRect = ui.transform.GetChild(0) as RectTransform;
                if (s_transitRect != null)
                {
                    var fitter = s_transitRect.GetComponent<ContentSizeFitter>();
                    if (fitter != null)
                    {
                        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    }
                    
                    s_transitRect.sizeDelta = new Vector2(318, 51);
                    s_transitImage = s_transitRect.GetComponent<Image>();
                    ApplyAnchorPosition(s_transitRect, pos == Position.Default ? Position.MiddleCenter : pos);
                    
                    if (IsUnityAlive(s_transitImage))
                    {
                        s_transitImage.color = color.From;
                    }
                }

                s_transitText = ui.GetComponentInChildren<TextMeshProUGUI>();
                if (IsUnityAlive(s_transitText))
                {
                    s_transitText.enableAutoSizing = false;
                    s_transitText.fontSize = 22;
                    s_transitText.text = label;
                }

                s_transitLoopActive = false;

                if (seconds > 0f)
                {
                    var timer = new CustomTimer();
                    timer.StartCountdown(seconds, label);

                    timer.OnTick += (span, formatted, p01) =>
                        ApplyTick(s_transitText, s_transitImage, color, label, span, formatted, p01);

                    timer.OnCompleted += () =>
                    {
                        try
                        {
                            if (ui != null && ui.isActiveAndEnabled)
                            {
                                ui.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.LogSource.LogWarning($"TransitPanel close warn: {ex.Message}");
                        }
                    };

                    return timer;
                }

                s_transitLoopSpec = color;
                s_transitLoopActive = color.IsGradient;
                return null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[VFX_UI.TransitPanel] {ex.Message}");
                return null;
            }
        }

        public static void HideTransitPanel()
        {
            try
            {
                var ui = MonoBehaviourSingleton<GameUI>.Instance?.LocationTransitTimerPanel;
                if (ui != null && ui.isActiveAndEnabled)
                {
                    ui.Close();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"HideTransitPanel warn: {ex.Message}");
            }
            finally
            {
                s_transitLoopActive = false;
            }
        }

        public static void EnsureTransitPanelPosition()
        {
            if (IsUnityAlive(s_transitRect))
            {
                ApplyAnchorPosition(s_transitRect, Position.MiddleCenter);
            }
        }

        //====================[ ObjectivePanel (BattleUIPanelExtraction) ]====================
        public static CustomTimer ObjectivePanel(Color color, Position pos, string label, float seconds = 0f) =>
            ObjectivePanel((ColorSpec)color, pos, label, seconds);

        public static CustomTimer ObjectivePanel(ColorSpec color, Position pos, string label, float seconds = 0f)
        {
            var gameUI = MonoBehaviourSingleton<GameUI>.Instance;
            if (gameUI == null)
            {
                return null;
            }

            try
            {
                s_extractionPanel = IsUnityAlive(s_extractionPanel) ? s_extractionPanel : gameUI.BattleUiPanelExtraction;
                if (s_extractionPanel == null)
                {
                    return null;
                }

                s_extractionPanel.Show(label);

                if (!IsUnityAlive(s_extractionRect))
                {
                    s_extractionRect  = s_extractionPanel.transform.GetChild(0) as RectTransform;
                    if (s_extractionRect != null)
                    {
                        s_extractionImage = s_extractionRect.GetComponent<Image>();
                        s_extractionText  = s_extractionRect.GetComponentInChildren<TextMeshProUGUI>();
                    }
                }

                if (IsUnityAlive(s_extractionRect))
                {
                    ApplyAnchorPosition(s_extractionRect, pos == Position.Default ? Position.BottomCenter : pos);
                }

                if (IsUnityAlive(s_extractionImage))
                {
                    s_extractionImage.color = color.From;
                }
                
                if (IsUnityAlive(s_extractionText))
                {
                    s_extractionText.enableAutoSizing = false;
                    s_extractionText.fontSize = 22;
                    s_extractionText.text = label;
                }

                s_extractionLoopActive = false;
                UnwireAndStopExtractionTimer();

                if (seconds > 0f)
                {
                    s_extractionTimer = new CustomTimer();
                    s_extractionTimer.StartCountdown(seconds, label);

                    s_extractionOnTick = (span, formatted, p01) =>
                        ApplyTick(s_extractionText, s_extractionImage, color, label, span, formatted, p01);

                    s_extractionOnDone = () =>
                    {
                        try
                        {
                            s_extractionPanel?.Close();
                        }
                        catch (Exception ex)
                        {
                            Plugin.LogSource.LogWarning($"ObjectivePanel close warn: {ex.Message}");
                        }
                    };

                    s_extractionTimer.OnTick += s_extractionOnTick;
                    s_extractionTimer.OnCompleted += s_extractionOnDone;
                    
                    return s_extractionTimer;
                }

                s_extractionLoopSpec = color;
                s_extractionLoopActive = color.IsGradient;
                return null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[VFX_UI.ObjectivePanel] {ex.Message}");
                return null;
            }
        }

        public static void HideObjectivePanel()
        {
            try
            {
                s_extractionPanel?.Close();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"HideObjectivePanel warn: {ex.Message}");
            }
            finally
            {
                s_extractionLoopActive = false;
                UnwireAndStopExtractionTimer();
            }
        }

        //====================[ Bulk Cleanup ]====================
        public static void HideAll()
        {
            HideTransitPanel();
            HideObjectivePanel();
        }

        //====================[ Helpers ]====================
        private static void ApplyTick(TextMeshProUGUI text, Image img, ColorSpec color, string label, TimeSpan span, string formatted, float p01)
        {
            if (IsUnityAlive(text))
            {
                if (label.IndexOf('{') >= 0)
                {
                    try
                    {
                        text.text = string.Format(label, Math.Max(0f, (float)span.TotalSeconds));
                    }
                    catch
                    {
                        text.text = $"{label}: {formatted}";
                    }
                }
                else
                {
                    text.text = $"{label}: {formatted}";
                }
            }

            if (IsUnityAlive(img))
            {
                if (color.IsGradient && p01 >= 0f)
                {
                    img.color = LerpPreserveA(color.From, color.To, p01);
                }
                else
                {
                    img.color = color.From;
                }
            }
        }

        
        private static void ApplyAnchorPosition(RectTransform panel, Position pos)
        {
            Vector2 anchor, pivot, offset;
            
            switch (pos)
            {
                case Position.TopLeft:       anchor = pivot = new Vector2(0f,   1f);   offset = new Vector2( 100, -50); break;
                case Position.TopCenter:     anchor = pivot = new Vector2(0.5f, 1f);   offset = new Vector2(   0, -50); break;
                case Position.TopRight:      anchor = pivot = new Vector2(1f,   1f);   offset = new Vector2(-100, -50); break;
                case Position.MiddleLeft:    anchor = pivot = new Vector2(0f,   0.5f); offset = new Vector2( 100,   0); break;
                case Position.MiddleCenter:  anchor = pivot = new Vector2(0.5f, 0.5f); offset = new Vector2(   0,   0); break;
                case Position.MiddleRight:   anchor = pivot = new Vector2(1f,   0.5f); offset = new Vector2(-100,   0); break;
                case Position.BottomLeft:    anchor = pivot = new Vector2(0f,   0f);   offset = new Vector2( 100,  50); break;
                case Position.BottomCenter:  anchor = pivot = new Vector2(0.5f, 0f);   offset = new Vector2(   0,  50); break;
                case Position.BottomRight:   anchor = pivot = new Vector2(1f,   0f);   offset = new Vector2(-100,  50); break;
                default:                     anchor = pivot = new Vector2(0.5f, 0.5f); offset = new Vector2(   0,   0); break;
            }
            
            panel.anchorMin = panel.anchorMax = anchor;
            panel.pivot = pivot;
            panel.anchoredPosition = offset;
        }

        private static bool IsUnityAlive(UnityEngine.Object o) => o != null;

        private static Color LerpPreserveA(Color a, Color b, float t)
        {
            float alpha = a.a;
            var c = Color.Lerp(a, b, t);
            c.a = alpha;
            return c;
        }

        private static void UnwireAndStopExtractionTimer()
        {
            try
            {
                if (s_extractionTimer != null)
                {
                    if (s_extractionOnTick != null)
                    {
                        s_extractionTimer.OnTick -= s_extractionOnTick;
                    }
                    
                    if (s_extractionOnDone != null)
                    {
                        s_extractionTimer.OnCompleted -= s_extractionOnDone;
                    }
                    
                    s_extractionTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"UnwireAndStopExtractionTimer warn: {ex.Message}");
            }
            finally
            {
                s_extractionTimer = null;
                s_extractionOnTick = null;
                s_extractionOnDone = null;
            }
        }

        //====================[ Driver Tick ]====================
        internal static void InternalUpdate()
        {
            if (s_extractionTimer != null && s_extractionTimer.IsRunning)
            {
                s_extractionTimer.Update();
            }

            // 15s ping-pong gradient loop (unscaled)
            float tPing = Mathf.PingPong(Time.unscaledTime / 7.5f, 1f);

            if (s_transitLoopActive && IsUnityAlive(s_transitImage) && s_transitLoopSpec.IsGradient)
            {
                s_transitImage.color = LerpPreserveA(s_transitLoopSpec.From, s_transitLoopSpec.To, tPing);
            }

            if (s_extractionLoopActive && IsUnityAlive(s_extractionImage) && s_extractionLoopSpec.IsGradient)
            {
                s_extractionImage.color = LerpPreserveA(s_extractionLoopSpec.From, s_extractionLoopSpec.To, tPing);
            }
        }

        private static void EnsureDriver()
        {
            try
            {
                if (UnityEngine.Object.FindObjectOfType<VfxUiDriver>() != null)
                {
                    return;
                }
                
                var go = new GameObject("VFX_UI_Driver");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<VfxUiDriver>();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"EnsureDriver error: {ex.Message}");
            }
        }
    }

    //====================[ VfxUiDriver ]====================
    internal sealed class VfxUiDriver : MonoBehaviour
    {
        private void Update() => VFX_UI.InternalUpdate();
    }
}