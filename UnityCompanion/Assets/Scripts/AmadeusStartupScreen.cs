using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace KurisuCompanion
{
    internal sealed class AmadeusStartupScreen : MonoBehaviour
    {
        private Texture2D wallpaper;
        private Texture2D phoneFrame;
        private GUIStyle captionStyle;
        private GUIStyle percentStyle;
        private float progress;
        private string caption = "正在启动 Amadeus";

        private void Awake()
        {
            wallpaper = Resources.Load<Texture2D>("AmadeusWallpaper");
        }

        private IEnumerator Start()
        {
            if (!Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--smoke-startup")) yield break;
            yield return new WaitForSecondsRealtime(.25f);
            yield return new WaitForEndOfFrame();
            CaptureSmoke("startup-smoke.png");
            yield return new WaitForSecondsRealtime(.4f);
            yield return new WaitForEndOfFrame();
            CaptureSmoke("startup-smoke-later.png");
            yield return new WaitForSecondsRealtime(.4f);
            yield return new WaitForEndOfFrame();
            CaptureSmoke("startup-smoke-third.png");
            Application.Quit();
        }

        private static void CaptureSmoke(string name)
        {
            string path = Path.Combine(Application.persistentDataPath, name);
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, screenshot.EncodeToPNG());
            UnityEngine.Object.Destroy(screenshot);
            Debug.Log("AMADEUS_STARTUP_SCREENSHOT=" + path);
        }

        public void SetProgress(float value, string message)
        {
            progress = Mathf.Clamp01(Mathf.Max(progress, value));
            if (!string.IsNullOrEmpty(message)) caption = message;
        }

        private void OnGUI()
        {
            GUI.depth = -1000;
            float width = Screen.width, height = Screen.height;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (wallpaper != null)
            {
                float imageWidth = Mathf.Max(width * 1.6f, height * 1.08f);
                float imageHeight = imageWidth * wallpaper.height / wallpaper.width;
                var imageRect = new Rect((width - imageWidth) * .5f,
                    height * .44f - imageHeight * .5f, imageWidth, imageHeight);
                GUI.DrawTexture(imageRect, wallpaper, ScaleMode.ScaleToFit);
            }

            float barWidth = Mathf.Min(width * .62f, 390f);
            float barY = height * .62f;
            float barX = (width - barWidth) * .5f;
            float barHeight = Mathf.Max(5f, width * .009f);
            GUI.color = new Color(.23f, .18f, .10f);
            GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), Texture2D.whiteTexture);
            GUI.color = new Color(1f, .62f, .12f);
            GUI.DrawTexture(new Rect(barX, barY, barWidth * progress, barHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                percentStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            }
            captionStyle.fontSize = percentStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(width * .023f, 12f, 18f));
            captionStyle.normal.textColor = new Color(.78f, .74f, .64f);
            percentStyle.normal.textColor = new Color(1f, .62f, .12f);
            GUI.Label(new Rect(barX, barY + 16f, barWidth, 26f), caption, captionStyle);
            GUI.Label(new Rect(barX, barY - 28f, barWidth, 24f),
                Mathf.RoundToInt(progress * 100f) + "%", percentStyle);

            int frameWidth = Mathf.RoundToInt(width), frameHeight = Mathf.RoundToInt(height);
            if (phoneFrame == null || phoneFrame.width != frameWidth || phoneFrame.height != frameHeight)
            {
                if (phoneFrame != null) Destroy(phoneFrame);
                phoneFrame = PhoneFrameArtwork.Create(frameWidth, frameHeight);
            }
            GUI.DrawTexture(new Rect(0, 0, width, height), phoneFrame);
        }

        private void OnDestroy()
        {
            if (phoneFrame != null) Destroy(phoneFrame);
        }
    }
}
