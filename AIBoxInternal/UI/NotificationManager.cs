using UnityEngine;
using System.Collections.Generic;

namespace AIBoxInternal.UI
{
    public class NotificationData
    {
        public string Title;
        public string Message;
        public float CreatedTime;
        public float Duration;
        public Color BackgroundColor;
    }

    public class NotificationManager : MonoBehaviour
    {
        public static NotificationManager Instance { get; private set; }
        private List<NotificationData> _activeNotifications = new List<NotificationData>();
        
        private GUIStyle _toastStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _msgStyle;
        private bool _stylesInitialized = false;
        private Texture2D _whiteTex; // Cached — never recreated per frame

        public bool IsEnabled = true;

        void Awake()
        {
            Instance = this;
            // Create ONE white texture used for all notifications — tinted via GUI.color
            _whiteTex = new Texture2D(2, 2);
            Color[] pixels = new Color[] { Color.white, Color.white, Color.white, Color.white };
            _whiteTex.SetPixels(pixels);
            _whiteTex.Apply();
        }

        public void Show(string title, string message, Color color, float duration = 5f)
        {
            if (!IsEnabled) return;

            _activeNotifications.Add(new NotificationData
            {
                Title = title,
                Message = message,
                CreatedTime = Time.time,
                Duration = duration,
                BackgroundColor = color
            });
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _toastStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14, richText = true };
            _msgStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };

            _stylesInitialized = true;
        }

        private void OnDestroy()
        {
            if (_whiteTex != null) Destroy(_whiteTex);
        }

        void OnGUI()
        {
            if (!IsEnabled || _activeNotifications.Count == 0) return;

            InitStyles();

            float yOffset = Screen.height - 50; // Start near bottom
            float width = 300;
            float padding = 10;

            for (int i = _activeNotifications.Count - 1; i >= 0; i--)
            {
                var note = _activeNotifications[i];
                float age = Time.time - note.CreatedTime;
                
                if (age > note.Duration)
                {
                    _activeNotifications.RemoveAt(i);
                    continue;
                }

                // Fade out in last 1 second
                float alpha = 1f;
                if (age > note.Duration - 1f)
                {
                    alpha = note.Duration - age;
                }

                // MEMORY LEAK FIX: Use GUI.color to tint the single cached white texture
                // instead of creating a new Texture2D every frame per notification
                Color bgColor = note.BackgroundColor;
                bgColor.a = alpha * 0.9f;
                GUI.color = bgColor;
                _toastStyle.normal.background = _whiteTex;

                float height = _msgStyle.CalcHeight(new GUIContent(note.Message), width - 20) + 40;
                yOffset -= (height + padding);

                Rect rect = new Rect(Screen.width - width - 20, yOffset, width, height);
                
                GUI.Box(rect, "", _toastStyle);
                
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.Label(new Rect(rect.x + 10, rect.y + 5, width - 20, 20), note.Title, _titleStyle);
                GUI.Label(new Rect(rect.x + 10, rect.y + 25, width - 20, height - 30), note.Message, _msgStyle);
                
                GUI.color = Color.white;
            }
        }
    }
}
