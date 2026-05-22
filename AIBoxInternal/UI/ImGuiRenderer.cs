using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using AIBoxInternal.Core;

namespace AIBoxInternal.UI
{
    public class ImGuiRenderer : MonoBehaviour
    {
        private Rect _windowRect = new Rect(20, 20, 850, 650);
        private Vector2 _scrollPos;

        // Customization
        private float _bgOpacity = 0.70f;
        private int _accentColorIndex = 0;
        private int _fontSize = 14;
        private bool _showNewsTicker = true;
        private bool _showMapOverlays = true;

        // Advanced Toggles
        private bool _enableInterview = true;
        private bool _enableMap = true;
        private bool _enableDiplomacy = true;

        private Color[] _accentColors = new Color[] {
            new Color(0.3f, 0.5f, 0.8f, 1f), // Blue
            new Color(0.8f, 0.3f, 0.3f, 1f), // Red
            new Color(0.3f, 0.8f, 0.4f, 1f), // Green
            new Color(0.6f, 0.3f, 0.8f, 1f), // Purple
            new Color(0.8f, 0.6f, 0.2f, 1f)  // Gold
        };

        // Styles
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _sparklineStyle;

        // Style cache tracking — avoids recreating Texture2D every frame
        private bool _stylesInitialized = false;
        private float _lastBgOpacity = -1f;
        private int _lastAccentColorIndex = -1;
        private int _lastFontSize = -1;

        // Cached textures for cleanup
        private List<Texture2D> _cachedTextures = new List<Texture2D>();

        private bool _isMinimized = false;
        private bool _isResizing = false;
        private float _windowHeight = 650;
        private float _windowWidth = 850;
        private Rect _resizeHandleRect;

        // Sorting
        private enum SortMode { Population, Army, Treasury, Happiness }
        private SortMode _currentSort = SortMode.Population;
        private bool _sortAscending = false;

        private enum Tab { GlobalLogs, Economy, Analytics, Military, Interview, Map, Diplomacy, Config, Settings, About }
        private Tab _currentTab = Tab.GlobalLogs;

        // Tracks which kingdoms have their "Data" panel expanded in GlobalLogs
        private HashSet<string> _expandedDataPanels = new HashSet<string>();

        private string T(string en, string ru)
        {
            return Core.GlobalSettings.Language == Core.GameLanguage.Russian ? ru : en;
        }

        private string GetGradientText(string text, Color start, Color end)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                float t = text.Length > 1 ? (float)i / (text.Length - 1) : 0f;
                Color c = Color.Lerp(start, end, t);
                string hex = ColorUtility.ToHtmlStringRGB(c);
                sb.Append($"<color=#{hex}>{text[i]}</color>");
            }
            return sb.ToString();
        }

        private int _maxChatHistory = 100;

        void OnGUI()
        {
            InitStyles();
            if (_showNewsTicker) DrawNewsTicker();
            if (_showMapOverlays) DrawMapOverlays();

            float height = _isMinimized ? 25 : _windowHeight;
            _windowRect.height = height;
            _windowRect.width = _windowWidth;
            
            // Draw World Tension Bar at the top
            float tension = Core.GlobalState.WorldTension;
            GUI.Box(new Rect(Screen.width - 310, 5, 300, 25), "", _headerStyle);
            GUI.Box(new Rect(Screen.width - 310, 5, 300 * tension, 25), "", _buttonStyle);
            GUI.Label(new Rect(Screen.width - 305, 7, 300, 20), $"<b>DEFCON / Tension: {tension:P0} ({Core.GlobalState.CurrentPhase})</b>", _labelStyle);

            string windowTitle = T("AIBox Internal - Grand Strategy Dashboard", "AIBox Внутренний - Панель глобальной стратегии") + " | " + GetGradientText("Made By BlackW1the", Color.cyan, Color.magenta);
            _windowRect = GUI.Window(0, _windowRect, DrawWindow, windowTitle, _windowStyle);
            
            if (!_isMinimized && !_isResizing)
            {
                _windowHeight = _windowRect.height;
                _windowWidth = _windowRect.width;
            }
        }

        private float _tickerPos = 0;
        private void DrawNewsTicker()
        {
            if (Core.GlobalState.GlobalNews.Count == 0) return;

            string fullText = string.Join("  |  ", Core.GlobalState.GlobalNews);
            GUI.Box(new Rect(0, Screen.height - 30, Screen.width, 30), "", _headerStyle);
            
            _tickerPos -= Time.deltaTime * 50f;
            if (_tickerPos < -3000) _tickerPos = Screen.width;

            GUI.Label(new Rect(_tickerPos, Screen.height - 25, 6000, 25), $"<b>BREAKING NEWS:</b> {fullText}", _labelStyle);
        }

        private void DrawMapOverlays()
        {
            if (World.world == null) return;
            var brains = MainController.Instance.Engine.GetBrains();
            foreach (Kingdom k in World.world.kingdoms.list)
            {
                if (!k.isAlive() || !k.isCiv() || k.capital == null) continue;
                if (!brains.ContainsKey(k)) continue;

                var brain = brains[k];
                WorldTile capitalTile = k.capital.getTile();
                if (capitalTile == null) continue;

                Vector3 screenPos = Camera.main.WorldToScreenPoint(capitalTile.posV3);
                if (screenPos.z < 0) continue; // Behind camera

                Rect iconRect = new Rect(screenPos.x - 20, Screen.height - screenPos.y - 60, 40, 40);
                string icon = GetStanceIcon(brain.Stance);
                
                GUI.Box(iconRect, icon, _headerStyle);
                GUI.Label(new Rect(iconRect.x - 30, iconRect.y + 40, 100, 20), $"<color=white>{brain.Focus}</color>", _labelStyle);
            }
        }

        private string GetStanceIcon(Core.MilitaryStance stance)
        {
            switch(stance) {
                case Core.MilitaryStance.Blitzkrieg: return "⚡";
                case Core.MilitaryStance.Guerrilla: return "🌿";
                case Core.MilitaryStance.ScorchedEarth: return "🔥";
                default: return "🛡️";
            }
        }

        private void InitStyles()
        {
            // MEMORY LEAK FIX: Only rebuild styles when settings actually change
            bool needsRebuild = !_stylesInitialized
                || _lastBgOpacity != _bgOpacity
                || _lastAccentColorIndex != _accentColorIndex
                || _lastFontSize != _fontSize;

            if (!needsRebuild) return;

            // Destroy old cached textures before creating new ones
            foreach (var tex in _cachedTextures)
                if (tex != null) Destroy(tex);
            _cachedTextures.Clear();

            Color bgColor = new Color(0.08f, 0.08f, 0.08f, _bgOpacity);
            Color headerColor = new Color(0.15f, 0.15f, 0.15f, _bgOpacity);
            Color panelColor = new Color(0.12f, 0.12f, 0.12f, _bgOpacity);
            Color accentColor = _accentColors[_accentColorIndex];
            Color textColor = Color.white;

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = CacheTex(MakeTex(2, 2, bgColor));
            _windowStyle.focused.background = CacheTex(MakeTex(2, 2, bgColor));
            _windowStyle.onNormal.background = CacheTex(MakeTex(2, 2, bgColor));
            _windowStyle.richText = true;

            _headerStyle = new GUIStyle(GUI.skin.box);
            _headerStyle.normal.background = CacheTex(MakeTex(2, 2, headerColor));
            _headerStyle.normal.textColor = textColor;
            _headerStyle.fontSize = _fontSize;
            _headerStyle.fontStyle = FontStyle.Bold;

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = CacheTex(MakeTex(2, 2, panelColor));
            _panelStyle.normal.textColor = textColor;
            _panelStyle.fontSize = _fontSize;

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.normal.background = CacheTex(MakeTex(2, 2, headerColor));
            _buttonStyle.hover.background = CacheTex(MakeTex(2, 2, accentColor));
            _buttonStyle.active.background = CacheTex(MakeTex(2, 2, accentColor));
            _buttonStyle.normal.textColor = textColor;
            _buttonStyle.fontSize = _fontSize;

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.normal.textColor = textColor;
            _labelStyle.fontSize = _fontSize;
            _labelStyle.richText = true;
            _labelStyle.wordWrap = true;

            _sparklineStyle = new GUIStyle(GUI.skin.box);
            _sparklineStyle.normal.background = CacheTex(MakeTex(2, 2, accentColor));

            // Update cache state
            _lastBgOpacity = _bgOpacity;
            _lastAccentColorIndex = _accentColorIndex;
            _lastFontSize = _fontSize;
            _stylesInitialized = true;
        }

        private Texture2D CacheTex(Texture2D tex)
        {
            _cachedTextures.Add(tex);
            return tex;
        }

        private void OnDestroy()
        {
            // Clean up all GPU textures when the mod unloads
            foreach (var tex in _cachedTextures)
                if (tex != null) Destroy(tex);
            _cachedTextures.Clear();
            if (_lineTex != null) Destroy(_lineTex);
        }

        private void DrawWindow(int windowID)
        {
            if (GUI.Button(new Rect(_windowRect.width - 25, 2, 20, 18), _isMinimized ? "+" : "-", _buttonStyle))
            {
                _isMinimized = !_isMinimized;
            }

            if (_isMinimized)
            {
                GUI.DragWindow(new Rect(0, 0, 10000, 25));
                return;
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.BeginVertical();
            
            // Tab Bar
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_currentTab == Tab.GlobalLogs, T("Logs", "Логи"), _buttonStyle)) _currentTab = Tab.GlobalLogs;
            if (GUILayout.Toggle(_currentTab == Tab.Economy, T("Economy", "Экономика"), _buttonStyle)) _currentTab = Tab.Economy;
            if (GUILayout.Toggle(_currentTab == Tab.Analytics, T("Data Hub", "Центр Данных"), _buttonStyle)) _currentTab = Tab.Analytics;
            if (GUILayout.Toggle(_currentTab == Tab.Military, T("Military", "Армия"), _buttonStyle)) _currentTab = Tab.Military;
            
            if (_enableInterview)
                if (GUILayout.Toggle(_currentTab == Tab.Interview, T("Interview", "Интервью"), _buttonStyle)) _currentTab = Tab.Interview;
            if (_enableMap)
                if (GUILayout.Toggle(_currentTab == Tab.Map, T("Map", "Карта"), _buttonStyle)) _currentTab = Tab.Map;
            if (GUILayout.Toggle(_currentTab == Tab.Diplomacy, T("Diplomacy", "Дипломатия"), _buttonStyle)) _currentTab = Tab.Diplomacy;

            if (GUILayout.Toggle(_currentTab == Tab.Config, T("AI Setup", "Настройки ИИ"), _buttonStyle)) _currentTab = Tab.Config;

            if (GUILayout.Toggle(_currentTab == Tab.Settings, T("Settings", "Настройки"), _buttonStyle)) _currentTab = Tab.Settings;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_currentTab)
            {
                case Tab.GlobalLogs: DrawGlobalLogs(); break;
                case Tab.Economy: DrawEconomy(); break;
                case Tab.Analytics: DrawAnalytics(); break;
                case Tab.Military: DrawMilitary(); break;
                case Tab.Interview: DrawInterview(); break;
                case Tab.Map: DrawMap(); break;
                case Tab.Diplomacy: DrawDiplomacyWeb(); break;
                case Tab.Config: DrawConfig(); break;
                case Tab.Settings: DrawSettings(); break;
                case Tab.About: DrawAbout(); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // Resize handle FIX: explicitly capture new dimensions during drag
            _resizeHandleRect = new Rect(_windowRect.width - 20, _windowRect.height - 20, 20, 20);
            GUI.Label(_resizeHandleRect, "↘", _labelStyle);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && _resizeHandleRect.Contains(e.mousePosition))
            {
                _isResizing = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _isResizing = false;
            }
            else if (e.type == EventType.MouseDrag && _isResizing)
            {
                _windowRect.width += e.delta.x;
                _windowRect.height += e.delta.y;
                
                _windowRect.width = Mathf.Max(600, _windowRect.width);
                _windowRect.height = Mathf.Max(400, _windowRect.height);
                
                _windowWidth = _windowRect.width;
                _windowHeight = _windowRect.height;
                e.Use();
            }
        }

        // --- TAB: GLOBAL LOGS ---
        private void DrawGlobalLogs()
        {
            var brains = MainController.Instance.Engine.GetBrains();
            foreach (Kingdom k in World.world.kingdoms.list)
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (!brains.ContainsKey(k)) continue;

                var brain = brains[k];

                GUILayout.BeginVertical(_panelStyle);
                
                string race = (k.king != null && k.king.asset != null) ? k.king.asset.id : "Civ";
                GUILayout.Label($"<color=yellow><b>[Crown] {k.king?.getName() ?? "None"} of {k.name} ({race})</b></color>", _headerStyle);

                // Show unread diplomatic mail
                int unreadCount = MailRegistry.CountUnread(k.name);
                if (unreadCount > 0)
                {
                    GUILayout.Label($"<color=#99ccff><b>[Mail] {unreadCount} unread message(s)</b></color>", _labelStyle);
                    var unread = MailRegistry.GetUnreadInbox(k.name).Take(3);
                    foreach (var msg in unread)
                    {
                        string color = msg.OpinionShift < 0 ? "red" : (msg.OpinionShift > 0 ? "green" : "#99ccff");
                        GUILayout.Label($"  <color={color}>From {msg.SenderKingdom}: {msg.Subject}</color>", _labelStyle);
                    }
                }

                // Show recent outgoing mail
                var sent = MailRegistry.GetSent(k.name).TakeLast(3);
                foreach (var msg in sent)
                {
                    GUILayout.Label($"<color=#cccccc><b>[Sent]</b> To {msg.RecipientKingdom}: {msg.Subject}</color>", _labelStyle);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#ffcc66><i><b>[Think]</b> \"{brain.LastThink}\"</i></color>", _labelStyle);
                if (GUILayout.Button(T("TTS", "Озвучить"), _buttonStyle, GUILayout.Width(100)))
                {
                    PlayTTS(brain.LastThink);
                }
                GUILayout.EndHorizontal();

                GUILayout.Label($"<color=#66ff66><b>[Action]</b> {brain.LastAction}</color>", _labelStyle);

                // --- DATA BUTTON: Expands to show event tracker log ---
                bool isExpanded = _expandedDataPanels.Contains(k.name);
                string dataBtnLabel = isExpanded ? T("▲ Hide Data", "▲ Скрыть Данные") : T("▼ Data", "▼ Данные");
                if (GUILayout.Button(dataBtnLabel, _buttonStyle))
                {
                    if (isExpanded) _expandedDataPanels.Remove(k.name);
                    else _expandedDataPanels.Add(k.name);
                }

                if (isExpanded)
                {
                    GUILayout.BeginVertical(_panelStyle);
                    string eventLog = brain.EventTracker.BuildEventLogString(maxEvents: 15);
                    if (string.IsNullOrEmpty(eventLog))
                    {
                        GUILayout.Label("<color=#888888><i>No events recorded yet.</i></color>", _labelStyle);
                    }
                    else
                    {
                        // Color-code event lines for readability
                        string[] lines = eventLog.Split('\n');
                        foreach (string line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            string trimmed = line.Trim();
                            if (trimmed.StartsWith("==="))
                            {
                                GUILayout.Label($"<color=#ffaa44><b>{trimmed}</b></color>", _labelStyle);
                            }
                            else if (trimmed.StartsWith("[YOU]"))
                            {
                                GUILayout.Label($"<color=#66ff66>{EscapeRichText(trimmed)}</color>", _labelStyle);
                            }
                            else if (trimmed.StartsWith("[GAME]"))
                            {
                                GUILayout.Label($"<color=#ff9966>{EscapeRichText(trimmed)}</color>", _labelStyle);
                            }
                            else if (trimmed.StartsWith("[FROM"))
                            {
                                GUILayout.Label($"<color=#99ccff>{EscapeRichText(trimmed)}</color>", _labelStyle);
                            }
                            else if (trimmed.StartsWith("[PENDING"))
                            {
                                GUILayout.Label($"<color=#ffcc66><b>{EscapeRichText(trimmed)}</b></color>", _labelStyle);
                            }
                            else
                            {
                                GUILayout.Label(EscapeRichText(trimmed), _labelStyle);
                            }
                        }
                    }
                    // --- City Priorities ---
                    if (brain.CityPriorities.Count > 0)
                    {
                        GUILayout.Label($"<color=#ffaa44><b>{T("City Priorities", "Приоритеты Городов")}</b></color>", _labelStyle);
                        foreach (var cp in brain.CityPriorities)
                        {
                            GUILayout.Label($"  {cp.Key}: <color=#66ff66>{cp.Value}</color>", _labelStyle);
                        }
                    }

                    // --- Active Plan ---
                    if (brain.CurrentPlan != null && brain.CurrentPlan.Status == PlanStatus.Active)
                    {
                        GUILayout.Label($"<color=#ffaa44><b>{T("Active Plan", "Активный План")}</b></color>", _labelStyle);
                        string turnsLabel = T("turns", "ходов");
                        GUILayout.Label($"  {brain.CurrentPlan.Name}: {brain.CurrentPlan.TurnsElapsed}/{brain.CurrentPlan.TargetTurns} {turnsLabel}", _labelStyle);
                        GUILayout.Label($"  {T("Target", "Цель")}: {brain.CurrentPlan.TargetKingdom} / {brain.CurrentPlan.TargetCity}", _labelStyle);
                        GUILayout.Label($"  {T("Next", "Следующий")}: {brain.CurrentPlan.NextStep}", _labelStyle);
                    }
                    else if (brain.CompletedPlans.Count > 0)
                    {
                        GUILayout.Label($"<color=#888888><b>{T("Recent Plans", "Недавние Планы")}</b></color>", _labelStyle);
                        foreach (var plan in brain.CompletedPlans.TakeLast(3))
                            GUILayout.Label($"  {plan}", _labelStyle);
                    }

                    // --- Combat Stance ---
                    if (brain.WarStance != CombatStance.Aggressive || !string.IsNullOrEmpty(brain.SiegeTargetCity))
                    {
                        GUILayout.Label($"<color=#ffaa44><b>{T("Combat Status", "Боевой Статус")}</b></color>", _labelStyle);
                        GUILayout.Label($"  {T("War Stance", "Боевая Стойка")}: {brain.WarStance}", _labelStyle);
                        if (!string.IsNullOrEmpty(brain.SiegeTargetCity))
                            GUILayout.Label($"  {T("Sieging", "Осада")}: <color=#ff6666>{brain.SiegeTargetCity}</color>", _labelStyle);
                    }

                    GUILayout.EndVertical();
                }

                GUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        /// <summary>
        /// Escapes angle brackets in text to prevent nested Unity rich-text tags from breaking.
        /// </summary>
        private string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("<", "\u2039").Replace(">", "\u203A");
        }

        private void PlayTTS(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Remove all markdown, brackets, and special characters. Keep only letters, numbers, and basic punctuation.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[^a-zA-Zа-яА-ЯёЁ0-9\s.,!?]+", "");
            
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aibox_tts.txt");
            System.IO.File.WriteAllText(tempFile, text, System.Text.Encoding.UTF8);
            
            string script = "Add-Type -AssemblyName System.Speech; " +
                            "$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                            "$ruVoice = $synth.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.Name -match 'ru-RU' } | Select-Object -First 1; " +
                            "if ($ruVoice) { $synth.SelectVoice($ruVoice.VoiceInfo.Name) } " +
                            $"$text = [System.IO.File]::ReadAllText('{tempFile.Replace("\\", "\\\\")}', [System.Text.Encoding]::UTF8); " +
                            "$synth.Speak($text)";
            
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
        }

        // --- TAB: ECONOMY ---
        private void DrawEconomy()
        {
            GUILayout.Label("<b>📊 KINGDOM RESOURCE TRACKER (Real Data)</b>", _headerStyle);
            GUILayout.Space(10);

            int totalPop = World.world.units.Count;
            int totalCivs = World.world.kingdoms.list.Count(k => k.isAlive() && k.isCiv());
            GUILayout.BeginHorizontal(_panelStyle);
            GUILayout.Label($"Global Population: {totalPop}", _labelStyle);
            GUILayout.Label($"Active Kingdoms: {totalCivs}", _labelStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(15);

            var brains = MainController.Instance.Engine.GetBrains();
            foreach (Kingdom k in World.world.kingdoms.list)
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (!brains.ContainsKey(k)) continue;

                var brain = brains[k];
                if (brain.MemoryBank == null) continue;

                GUILayout.BeginVertical(_panelStyle);
                GUILayout.Label($"<b><color=yellow>{k.name}</color></b> | {brain.MemoryBank.GetPopulationTrend()} | {brain.MemoryBank.GetMilitaryTrend()}", _labelStyle);

                // Resource sparklines
                foreach (var res in brain.MemoryBank.ResourceHistory.Keys.OrderBy(x => x))
                {
                    if (brain.MemoryBank.ResourceHistory[res].Count < 2) continue;

                    var vals = brain.MemoryBank.ResourceHistory[res].ToArray();
                    int current = vals[vals.Length - 1];
                    int old = vals[0];
                    string trend = current > old ? "<color=green>▲</color>" : (current < old ? "<color=red>▼</color>" : "<color=gray>━</color>");

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  {res}: {current} {trend}", _labelStyle, GUILayout.Width(180));
                    DrawSparkline(vals.Select(v => (float)v).ToArray(), 200, 20);
                    GUILayout.EndHorizontal();
                }

                // Spending summary
                string spending = brain.MemoryBank.GetSpendingSummary(Time.time - 120f);
                if (spending != "No recent spending.")
                    GUILayout.Label($"<size=11><i>Recent Spending: {spending}</i></size>", _labelStyle);

                GUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void DrawSparkline(float[] data, float width, float height)
        {
            if (data.Length < 2) return;
            
            float max = data.Max();
            float min = data.Min();
            float range = max - min;
            if (range == 0) range = 1;

            Rect rect = GUILayoutUtility.GetRect(width, height);
            GUI.Box(rect, "", _headerStyle);

            float stepX = width / (data.Length - 1);
            
            // Draw simple bar chart approximation
            for (int i = 0; i < data.Length; i++)
            {
                float normalized = (data[i] - min) / range;
                float barHeight = Mathf.Max(2, normalized * height);
                float yPos = rect.y + height - barHeight;
                float xPos = rect.x + (i * stepX);
                
                GUI.Box(new Rect(xPos, yPos, Mathf.Max(2, stepX - 1), barHeight), "", _sparklineStyle);
            }
        }

        // --- TAB: ANALYTICS (DATA HUB) ---
        private Kingdom _selectedKingdom;
        private string _whisperText = "";

        private void DrawAnalytics()
        {
            var brains = MainController.Instance.Engine.GetBrains();
            var validKingdoms = World.world.kingdoms.list.Where(k => k.isAlive() && k.isCiv() && brains.ContainsKey(k)).ToList();

            // Sort Controls
            GUILayout.BeginHorizontal(_headerStyle);
            if (GUILayout.Button(T("Name", "Имя"), _buttonStyle)) { _sortAscending = !_sortAscending; }
            if (GUILayout.Button("👥 " + T("Pop", "Нас"), _buttonStyle)) { _currentSort = SortMode.Population; _sortAscending = !_sortAscending; }
            if (GUILayout.Button("⚔️ " + T("Army", "Армия"), _buttonStyle)) { _currentSort = SortMode.Army; _sortAscending = !_sortAscending; }
            if (GUILayout.Button("💰 " + T("Gold", "Золото"), _buttonStyle)) { _currentSort = SortMode.Treasury; _sortAscending = !_sortAscending; }
            if (GUILayout.Button("😊 " + T("Happy", "Счастье"), _buttonStyle)) { _currentSort = SortMode.Happiness; _sortAscending = !_sortAscending; }
            GUILayout.EndHorizontal();

            // Apply Sorting
            switch (_currentSort)
            {
                case SortMode.Population:
                    validKingdoms = _sortAscending ? validKingdoms.OrderBy(k => k.getPopulationPeople()).ToList() : validKingdoms.OrderByDescending(k => k.getPopulationPeople()).ToList();
                    break;
                case SortMode.Army:
                    validKingdoms = _sortAscending ? validKingdoms.OrderBy(k => k.countTotalWarriors()).ToList() : validKingdoms.OrderByDescending(k => k.countTotalWarriors()).ToList();
                    break;
                case SortMode.Treasury:
                    validKingdoms = _sortAscending
                        ? validKingdoms.OrderBy(k => Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].TotalGold : 0).ToList()
                        : validKingdoms.OrderByDescending(k => Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].TotalGold : 0).ToList();
                    break;
                case SortMode.Happiness:
                    validKingdoms = _sortAscending
                        ? validKingdoms.OrderBy(k => brains[k].MemoryBank != null && brains[k].MemoryBank.KingdomAvgHappiness.Count > 0 ? brains[k].MemoryBank.KingdomAvgHappiness.Last() : 0).ToList()
                        : validKingdoms.OrderByDescending(k => brains[k].MemoryBank != null && brains[k].MemoryBank.KingdomAvgHappiness.Count > 0 ? brains[k].MemoryBank.KingdomAvgHappiness.Last() : 0).ToList();
                    break;
            }

            foreach (var k in validKingdoms)
            {
                var brain = brains[k];
                
                GUILayout.BeginHorizontal(_panelStyle);
                if (GUILayout.Button(k.name, _buttonStyle, GUILayout.Width(150)))
                {
                    _selectedKingdom = _selectedKingdom == k ? null : k;
                }
                // Read all values directly from RealTimeDB — guaranteed accurate
                int realPop  = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].Population  : k.getPopulationPeople();
                int realArmy = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].ArmySize    : k.countTotalWarriors();
                int realGold = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].TotalGold   : 0;
                int realFood = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].TotalFood   : 0;
                GUILayout.Label($"{realPop}", _labelStyle, GUILayout.Width(50));
                GUILayout.Label($"{realArmy}", _labelStyle, GUILayout.Width(50));
                GUILayout.Label($"<color=yellow>{realGold}g</color>", _labelStyle, GUILayout.Width(70));
                GUILayout.Label($"🍞 {realFood}", _labelStyle, GUILayout.Width(65));
                GUILayout.Label($"🎯 {brain.Focus} [{brain.Personality}]", _labelStyle);
                GUILayout.EndHorizontal();

                // Detailed Card
                if (_selectedKingdom == k)
                {
                    GUILayout.BeginVertical(_headerStyle);
                    
                    // City Table — use RealTimeDB for accurate per-city figures
                    GUILayout.Label(T("<b>City Matrix (Total: " + k.countCities() + ")</b>", "<b>Матрица Городов (Всего: " + k.countCities() + ")</b>"), _labelStyle);
                    var snap = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name] : null;
                    foreach(var city in brain.CityData.Values)
                    {
                        int cityRealGold = (snap != null && snap.CityGold.ContainsKey(city.Name)) ? snap.CityGold[city.Name] : city.Gold;
                        int cityRealPop  = (snap != null && snap.CityPop.ContainsKey(city.Name))  ? snap.CityPop[city.Name]  : 0;
                        bool isDistressed = cityRealGold < 200 || city.Food < 50;
                        string distress = isDistressed ? "<color=red>[DISTRESSED]</color>" : "<color=green>[STABLE]</color>";
                        GUILayout.Label($"🏙️ {city.Name} | Pop: {cityRealPop} | 💰 {cityRealGold}g | 🍞 {city.Food} {distress}", _labelStyle);
                    }

                    // Operations
                    if (brain.ActiveMissions.Count > 0)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label(T("<b>Active Operations</b>", "<b>Активные Операции</b>"), _labelStyle);
                        foreach(var m in brain.ActiveMissions)
                        {
                            GUILayout.Label($"🕵️ {m.Type} -> {m.TargetKingdom} ({m.Detail})", _labelStyle);
                            DrawProgressBar(m.Progress / 100f, 300, 15);
                        }
                    }

                    GUILayout.Space(5);
                    GUILayout.Label(T("<b>Divine Whisper (Override AI):</b>", "<b>Божественный Шепот (Переопределить ИИ):</b>"), _labelStyle);
                    GUILayout.BeginHorizontal();
                    _whisperText = GUILayout.TextField(_whisperText, GUILayout.Width(300));
                    if (GUILayout.Button(T("Send Whisper", "Отправить Шепот"), _buttonStyle))
                    {
                        Core.GlobalState.PendingWhispers[k.name] = _whisperText;
                        _whisperText = "";
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }
            }
        }

        private void DrawProgressBar(float progress, float width, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(width, height);
            GUI.Box(rect, "", _panelStyle);
            GUI.Box(new Rect(rect.x, rect.y, width * Mathf.Clamp01(progress), height), "", _sparklineStyle);
        }

        // --- TAB: MILITARY ---
        private void DrawMilitary()
        {
            GUILayout.Label("<b>WARS & CONFLICTS</b>", _headerStyle);
            GUILayout.Space(5);

            if (World.world.wars.list.Count == 0)
            {
                GUILayout.Label("<i>The world is currently at peace.</i>", _labelStyle);
            }
            else
            {
                foreach(var war in World.world.wars.list)
                {
                    GUILayout.BeginVertical(_panelStyle);
                    GUILayout.Label($"<color=red><b>{war.name}</b></color>", _headerStyle);
                    
                    var attackers = war.getAttackers();
                    var defenders = war.getDefenders();
                    
                    string aNames = string.Join(", ", attackers.Select(k => k.name));
                    string dNames = string.Join(", ", defenders.Select(k => k.name));
                    
                    int aArmy = attackers.Sum(k => k.countTotalWarriors());
                    int dArmy = defenders.Sum(k => k.countTotalWarriors());

                    GUILayout.Label($"<b>Attackers:</b> {aNames} (Army: {aArmy})", _labelStyle);
                    GUILayout.Label($"<b>Defenders:</b> {dNames} (Army: {dArmy})", _labelStyle);
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                }
            }
        }

        // --- TAB: INTERVIEW (Live Chat) ---
        private string _interviewInput = "";
        private Kingdom _interviewKingdom;
        private Vector2 _chatScrollPos;
        private bool _showQuickActions = true;

        private void DrawInterview()
        {
            GUILayout.Label("<b>ROYAL INTERVIEW</b>", _headerStyle);
            GUILayout.Space(5);
            GUILayout.Label("<i>Talk directly to the AI behind any King.</i>", _labelStyle);
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_interviewKingdom == null ? "Select Kingdom..." : _interviewKingdom.name, _buttonStyle))
            {
                _interviewKingdom = null;
            }
            if (_interviewKingdom != null && GUILayout.Button(T("Change", "Сменить"), _buttonStyle, GUILayout.Width(80)))
            {
                _interviewKingdom = null;
            }
            GUILayout.EndHorizontal();

            if (_interviewKingdom == null)
            {
                DrawKingdomSelector();
                return;
            }

            var activeBrain = MainController.Instance.Engine.GetBrains()[_interviewKingdom];
            if (activeBrain == null) return;

            // --- Kingdom Profile Card ---
            DrawKingdomProfileCard(_interviewKingdom, activeBrain);

            GUILayout.Space(5);

            // --- Quick Action Buttons ---
            if (_showQuickActions)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("State Report", "Доклад"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("Give me a full state report.", "Дай полный доклад о состоянии королевства."));
                if (GUILayout.Button(T("War Strategy", "Война"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("What is our war strategy?", "Какова наша военная стратегия?"));
                if (GUILayout.Button(T("Economy", "Экономика"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("How is our economy?", "Как обстоят дела с экономикой?"));
                if (GUILayout.Button(T("Diplomacy", "Дипломатия"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("Who are our friends and enemies?", "Кто наши друзья и враги?"));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("Trade Deal", "Торговля"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("Should we propose a trade deal to anyone?", "Стоит ли предложить торговлю кому-нибудь?"));
                if (GUILayout.Button(T("Plot Idea", "Заговор"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("Any plots you are considering?", "Какие заговоры ты рассматриваешь?"));
                if (GUILayout.Button(T("City Needs", "Города"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("Which city needs the most attention?", "Какому городу нужно больше внимания?"));
                if (GUILayout.Button(T("Personality", "Характер"), _buttonStyle))
                    SendQuickMessage(_interviewKingdom, activeBrain, T("Describe your personality and goals.", "Опиши свой характер и цели."));
                GUILayout.EndHorizontal();
            }

            // Toggle quick actions
            _showQuickActions = GUILayout.Toggle(_showQuickActions, T("Quick Actions", "Быстрые Действия"), _buttonStyle);

            GUILayout.Space(5);

            // --- Chat History Area ---
            GUILayout.Label($"<b>{T("Conversation", "Переписка")}</b>", _headerStyle);
            _chatScrollPos = GUILayout.BeginScrollView(_chatScrollPos, _panelStyle, GUILayout.Height(250));
            while (activeBrain.ChatHistory.Count > _maxChatHistory)
                activeBrain.ChatHistory.RemoveAt(0);

            if (activeBrain.ChatHistory.Count == 0)
            {
                GUILayout.Label($"<color=#888888><i>{T("No messages yet. Start the conversation!", "Пока нет сообщений. Начни разговор!")}</i></color>", _labelStyle);
            }
            else
            {
                foreach (var msg in activeBrain.ChatHistory)
                {
                    if (msg.StartsWith("You:"))
                    {
                        GUILayout.Label($"<color=#66ccff><b>{EscapeRichText(msg)}</b></color>", _labelStyle);
                    }
                    else if (msg.StartsWith("King "))
                    {
                        GUILayout.Label($"<color=#ffcc66>{EscapeRichText(msg)}</color>", _labelStyle);
                    }
                    else
                    {
                        GUILayout.Label(EscapeRichText(msg), _labelStyle);
                    }
                    GUILayout.Space(2);
                }
            }
            GUILayout.EndScrollView();

            // --- Input Area ---
            GUILayout.BeginHorizontal();
            _interviewInput = GUILayout.TextField(_interviewInput, GUILayout.Width(_windowRect.width - 220));
            if (GUILayout.Button(T("Send", "Отправить"), _buttonStyle, GUILayout.Width(70)))
            {
                SendInterviewMessage(_interviewKingdom, activeBrain, _interviewInput);
            }
            if (GUILayout.Button(T("Clear", "Очистить"), _buttonStyle, GUILayout.Width(70)))
            {
                activeBrain.ChatHistory.Clear();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawKingdomSelector()
        {
            GUILayout.Label($"<b>{T("Select a Kingdom to Interview", "Выбери Королевство для Интервью")}</b>", _headerStyle);
            var brains = MainController.Instance.Engine.GetBrains();
            foreach (Kingdom k in World.world.kingdoms.list.Where(x => x.isAlive() && x.isCiv() && brains.ContainsKey(x)))
            {
                var brain = brains[k];
                GUILayout.BeginHorizontal(_panelStyle);
                // Kingdom "avatar" using race icon and color
                string raceIcon = GetRaceIcon(k);
                string stanceIcon = GetStanceIcon(brain.Stance);
                GUILayout.Label($"<color=yellow><b>{raceIcon} {k.name}</b></color>", _labelStyle, GUILayout.Width(150));
                GUILayout.Label($"👑 {k.king?.getName() ?? "None"} | 👥 {k.getPopulationPeople()} | ⚔️ {k.countTotalWarriors()}", _labelStyle);
                if (GUILayout.Button(T("Select", "Выбрать"), _buttonStyle, GUILayout.Width(80)))
                {
                    _interviewKingdom = k;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawKingdomProfileCard(Kingdom k, KingdomBrain brain)
        {
            GUILayout.BeginVertical(_headerStyle);
            string raceIcon = GetRaceIcon(k);
            string stanceIcon = GetStanceIcon(brain.Stance);
            GUILayout.Label($"<color=yellow><b><size=16>{raceIcon} {k.name}</size></b></color> {stanceIcon}", _headerStyle);

            GUILayout.BeginHorizontal();
            // King info
            if (k.king != null)
            {
                GUILayout.Label($"👑 <b>{k.king.getName()}</b> | INT:{k.king.intelligence} DIP:{k.king.diplomacy} WAR:{k.king.warfare} STE:{k.king.stewardship}", _labelStyle);
            }
            else
            {
                GUILayout.Label($"👑 <b>{T("No King", "Нет Короля")}</b>", _labelStyle);
            }
            GUILayout.EndHorizontal();

            // Quick stats row
            GUILayout.BeginHorizontal();
            GUILayout.Label($"👥 {k.getPopulationPeople()}", _labelStyle);
            GUILayout.Label($"⚔️ {k.countTotalWarriors()}", _labelStyle);
            int totalGold = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].TotalGold : 0;
            GUILayout.Label($"💰 {totalGold}g", _labelStyle);
            GUILayout.Label($"🎯 {brain.Focus} [{brain.Personality}]", _labelStyle);
            GUILayout.EndHorizontal();

            // Recent events summary
            string recentEvents = brain.EventTracker.BuildEventLogString(maxEvents: 3);
            if (!string.IsNullOrEmpty(recentEvents))
            {
                string[] lines = recentEvents.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("===")).ToArray();
                if (lines.Length > 0)
                {
                    GUILayout.Label($"<color=#ff9966><b>{T("Recent Events", "Недавние События")}:</b></color>", _labelStyle);
                    foreach (var line in lines.Take(3))
                    {
                        GUILayout.Label($"  <color=#ff9966>{EscapeRichText(line.Trim())}</color>", _labelStyle);
                    }
                }
            }

            GUILayout.EndVertical();
        }

        private string GetRaceIcon(Kingdom k)
        {
            if (k.king != null && k.king.asset != null)
            {
                string race = k.king.asset.id?.ToLowerInvariant() ?? "";
                if (race.Contains("human")) return "🧑";
                if (race.Contains("elf")) return "🧝";
                if (race.Contains("orc")) return "👹";
                if (race.Contains("dwarf")) return "🧔";
                if (race.Contains("undead")) return "💀";
                if (race.Contains("cat")) return "🐱";
                if (race.Contains("dog")) return "🐶";
                if (race.Contains("wolf")) return "🐺";
                if (race.Contains("dragon")) return "🐲";
            }
            return "👤";
        }

        private void SendQuickMessage(Kingdom k, KingdomBrain brain, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Core.AIProviderClient.Instance.SendChatMessage(k, brain, message, (response) => { });
        }

        private void SendInterviewMessage(Kingdom k, KingdomBrain brain, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Core.AIProviderClient.Instance.SendChatMessage(k, brain, message, (response) => { });
            _interviewInput = "";
        }

        // --- TAB: HEAT MAP ---
        private int _mapMode = 0; // 0 = Wealth, 1 = Tension
        
        private void DrawMap()
        {
            GUILayout.Label("<b>STRATEGIC HEATMAP</b>", _headerStyle);
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_mapMode == 0, "Wealth Map", _buttonStyle)) _mapMode = 0;
            if (GUILayout.Toggle(_mapMode == 1, "Tension Map", _buttonStyle)) _mapMode = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            float mapSize = 400f;
            Rect mapRect = GUILayoutUtility.GetRect(mapSize, mapSize);
            GUI.Box(mapRect, "", _panelStyle);

            if (World.world == null) return;
            
            int worldW = MapBox.width;
            int worldH = MapBox.height;

            var brains = MainController.Instance.Engine.GetBrains();

            foreach (Kingdom k in World.world.kingdoms.list.Where(x => x.isAlive() && x.isCiv()))
            {
                if (!brains.ContainsKey(k)) continue;
                var brain = brains[k];

                foreach (City c in k.cities)
                {
                    WorldTile t = c.getTile();
                    if (t == null) continue;

                    float nx = (float)t.x / worldW;
                    float ny = (float)t.y / worldH;

                    float cx = mapRect.x + (nx * mapSize);
                    float cy = mapRect.y + (mapRect.height - (ny * mapSize)); // Invert Y

                    float radius = 15f; // Constant radius
                    radius = Mathf.Clamp(radius, 5, 20);

                    Color mapColor = Color.gray;
                    if (_mapMode == 0) // Wealth
                    {
                        int mapGold = Core.RealTimeDB.Kingdoms.ContainsKey(k.name) ? Core.RealTimeDB.Kingdoms[k.name].TotalGold : 0;
                        float wealthScore = Mathf.Clamp01(mapGold / 2000f);
                        mapColor = Color.Lerp(new Color(0, 0.2f, 0, 0.5f), new Color(0, 1f, 0, 0.8f), wealthScore);
                    }
                    else if (_mapMode == 1) // Tension
                    {
                        float tensionScore = brain.Stance == Core.MilitaryStance.Blitzkrieg ? 1f : (brain.Stance == Core.MilitaryStance.Guerrilla ? 0.6f : 0.2f);
                        bool hasWar = false;
                        var wList = World.world.wars.getWars(k);
                        if (wList != null) {
                            foreach (var w in wList) { hasWar = true; break; }
                        }
                        if (hasWar) tensionScore = 1f;
                        mapColor = Color.Lerp(new Color(0, 0, 0, 0.5f), new Color(1f, 0, 0, 0.8f), tensionScore);
                    }

                    GUI.color = mapColor;
                    GUI.Box(new Rect(cx - radius/2, cy - radius/2, radius, radius), "", _sparklineStyle);
                    GUI.color = Color.white;
                }
            }
            
            GUILayout.Label("<i>Approximate heatmap based on city centers and territory size.</i>", _labelStyle);
        }

        // --- TAB: DIPLOMACY WEB ---
        private void DrawDiplomacyWeb()
        {
            GUILayout.Label("<b>DIPLOMACY NODE GRAPH</b>", _headerStyle);
            
            float graphSize = 450f;
            Rect graphRect = GUILayoutUtility.GetRect(graphSize, graphSize);
            GUI.Box(graphRect, "", _panelStyle);

            if (World.world == null) return;
            
            var kingdoms = World.world.kingdoms.list.Where(x => x.isAlive() && x.isCiv()).ToList();
            if (kingdoms.Count == 0) return;

            Vector2 center = new Vector2(graphRect.x + graphSize / 2, graphRect.y + graphSize / 2);
            float radius = (graphSize / 2) - 40;

            Dictionary<Kingdom, Vector2> positions = new Dictionary<Kingdom, Vector2>();

            // Calculate circle positions
            for (int i = 0; i < kingdoms.Count; i++)
            {
                float angle = i * Mathf.PI * 2 / kingdoms.Count;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                positions[kingdoms[i]] = center + new Vector2(x, y);
            }

            // Draw Lines (Wars)
            foreach (var war in World.world.wars.list)
            {
                foreach (Kingdom a in war.getAttackers())
                {
                    foreach (Kingdom d in war.getDefenders())
                    {
                        if (positions.ContainsKey(a) && positions.ContainsKey(d))
                        {
                            DrawLine(positions[a], positions[d], 2, new Color(1, 0, 0, 0.5f));
                        }
                    }
                }
            }

            // Draw Nodes
            foreach (var kvp in positions)
            {
                Kingdom k = kvp.Key;
                Vector2 pos = kvp.Value;
                
                Rect btnRect = new Rect(pos.x - 30, pos.y - 15, 60, 30);
                GUI.color = Color.white; // k.getColor().color
                GUI.Box(btnRect, k.name, _buttonStyle);
                GUI.color = Color.white;
            }
        }

        private Texture2D _lineTex;
        private void DrawLine(Vector2 pointA, Vector2 pointB, float width, Color color)
        {
            if (_lineTex == null) _lineTex = MakeTex(1, 1, Color.white);

            Matrix4x4 matrix = GUI.matrix;
            Color savedColor = GUI.color;
            GUI.color = color;

            float angle = Mathf.Atan2(pointB.y - pointA.y, pointB.x - pointA.x) * 180f / Mathf.PI;
            float length = Vector2.Distance(pointA, pointB);

            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y, length, width), _lineTex);

            GUI.matrix = matrix;
            GUI.color = savedColor;
        }

        // --- TAB: CONFIG ---
        private enum AISetupMode { Global, PerKingdom }
        private AISetupMode _aiSetupMode = AISetupMode.Global;
        private Kingdom _selectedKingdomForConfig = null;

        private void DrawConfig()
        {
            var brains = MainController.Instance.Engine.GetBrains();

            // --- Mode Toggle ---
            GUILayout.Label("<b>AI SETUP MODE</b>", _headerStyle);
            GUILayout.BeginHorizontal(_panelStyle);
            if (GUILayout.Toggle(_aiSetupMode == AISetupMode.Global, T("GLOBAL AI (One for All)", "ГЛОБАЛЬНЫЙ ИИ (Один на всех)"), _buttonStyle))
                _aiSetupMode = AISetupMode.Global;
            if (GUILayout.Toggle(_aiSetupMode == AISetupMode.PerKingdom, T("PER-KINGDOM AI (Individual Agents)", "ИИ ДЛЯ КАЖДОГО КОРОЛЕВСТВА"), _buttonStyle))
                _aiSetupMode = AISetupMode.PerKingdom;
            GUILayout.EndHorizontal();

            // Sync the underlying flag
            Core.GlobalSettings.UseGlobalAI = (_aiSetupMode == AISetupMode.Global);

            GUILayout.Space(10);

            if (_aiSetupMode == AISetupMode.Global)
            {
                DrawGlobalAIConfig();
            }
            else
            {
                DrawPerKingdomAIConfig(brains);
            }
        }

        private void DrawGlobalAIConfig()
        {
            var g = Core.GlobalSettings.GlobalAI;

            GUILayout.Label("<b><color=yellow>GLOBAL AI CONFIGURATION</color></b>", _headerStyle);
            GUILayout.Label("<i>All kingdoms share this single AI model and prompt.</i>", _labelStyle);
            GUILayout.Space(5);

            GUILayout.BeginVertical(_panelStyle);

            // Provider
            GUILayout.Label("AI Provider:", _labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(g.Provider == Core.AIProvider.Internal, "Internal", _buttonStyle)) g.Provider = Core.AIProvider.Internal;
            if (GUILayout.Toggle(g.Provider == Core.AIProvider.Ollama, "Ollama", _buttonStyle)) g.Provider = Core.AIProvider.Ollama;
            if (GUILayout.Toggle(g.Provider == Core.AIProvider.OpenAI, "OpenAI", _buttonStyle)) g.Provider = Core.AIProvider.OpenAI;
            if (GUILayout.Toggle(g.Provider == Core.AIProvider.Claude, "Claude", _buttonStyle)) g.Provider = Core.AIProvider.Claude;
            GUILayout.EndHorizontal();

            if (g.Provider != Core.AIProvider.Internal)
            {
                GUILayout.Space(5);

                // Model
                GUILayout.Label("Model Name:", _labelStyle);
                g.Model = GUILayout.TextField(g.Model, GUILayout.Height(25));

                // Endpoint / API Key
                if (g.Provider == Core.AIProvider.Ollama)
                {
                    GUILayout.Label("Endpoint URL:", _labelStyle);
                    g.Endpoint = GUILayout.TextField(g.Endpoint, GUILayout.Height(25));
                }
                else
                {
                    GUILayout.Label("API Key:", _labelStyle);
                    g.ApiKey = GUILayout.PasswordField(g.ApiKey, '*', GUILayout.Height(25));
                }

                GUILayout.Space(5);

                // --- Rate Limiting ---
                GUILayout.Space(10);
                GUILayout.Label("<b>Rate Limiting</b>", _labelStyle);
                GUILayout.Label($"Min delay between calls: {g.MinDelayBetweenCalls:F1}s", _labelStyle);
                g.MinDelayBetweenCalls = GUILayout.HorizontalSlider(g.MinDelayBetweenCalls, 0.1f, 10f);
                GUILayout.Label($"Max calls per minute: {g.MaxCallsPerMinute}", _labelStyle);
                g.MaxCallsPerMinute = (int)GUILayout.HorizontalSlider(g.MaxCallsPerMinute, 1f, 120f);

                // --- Context Window ---
                GUILayout.Space(10);
                GUILayout.Label("<b>Context Window</b>", _labelStyle);
                GUILayout.Label($"Context window size: {g.ContextWindowTokens} tokens", _labelStyle);
                string[] contextOptions = new[] { "2048", "4096", "8192", "16384", "32768", "128000" };
                int contextIdx = System.Array.IndexOf(contextOptions, g.ContextWindowTokens.ToString());
                if (contextIdx < 0) contextIdx = 1;
                contextIdx = GUILayout.SelectionGrid(contextIdx, contextOptions, 3, _buttonStyle);
                g.ContextWindowTokens = int.Parse(contextOptions[contextIdx]);

                GUILayout.Label($"Max response tokens: {g.MaxResponseTokens}", _labelStyle);
                g.MaxResponseTokens = (int)GUILayout.HorizontalSlider(g.MaxResponseTokens, 64f, 2048f);

                // --- Token Budget ---
                GUILayout.Space(10);
                GUILayout.Label("<b>Token Budget</b>", _labelStyle);
                g.EnableTokenBudget = GUILayout.Toggle(g.EnableTokenBudget, "Enable token budget limit", _buttonStyle);
                if (g.EnableTokenBudget)
                {
                    GUILayout.Label($"Max tokens per minute: {g.MaxTokensPerMinute}", _labelStyle);
                    g.MaxTokensPerMinute = (int)GUILayout.HorizontalSlider(g.MaxTokensPerMinute, 1000f, 50000f);
                }

                // Custom Prompt
                GUILayout.Space(10);
                GUILayout.Label("<b>Custom System Prompt (Global Personality)</b>", _labelStyle);
                GUILayout.Label("<size=11><i>This prompt sets the personality for ALL kingdoms. Leave empty to use default game-generated personalities.</i></size>", _labelStyle);
                g.CustomSystemPrompt = GUILayout.TextArea(g.CustomSystemPrompt, GUILayout.Height(120));

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset Prompt to Default", _buttonStyle))
                {
                    g.CustomSystemPrompt = "";
                }
                if (GUILayout.Button("Load Example Prompt", _buttonStyle))
                {
                    g.CustomSystemPrompt = "You are a wise and cautious ruler. You prefer diplomacy over war. You care deeply about your people's happiness and will only fight to defend your realm.";
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<i>Internal AI uses built-in rule engine. No model needed.</i>", _labelStyle);
            }

            GUILayout.Space(10);

            // Quick actions
            GUILayout.Label("<b>Quick Actions</b>", _labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to All Kingdoms", _buttonStyle))
            {
                var brains = MainController.Instance.Engine.GetBrains();
                foreach (var kvp in brains)
                {
                    kvp.Value.Config.Provider = g.Provider;
                    kvp.Value.Config.Model = g.Model;
                    kvp.Value.Config.ApiKey = g.ApiKey;
                    kvp.Value.Config.Endpoint = g.Endpoint;
                    kvp.Value.Config.CustomSystemPrompt = g.CustomSystemPrompt;
                }
            }
            if (GUILayout.Button("Reset Global to Defaults", _buttonStyle))
            {
                g.Provider = Core.AIProvider.Internal;
                g.Model = "llama3";
                g.ApiKey = "";
                g.Endpoint = "http://localhost:11434/api/generate";
                g.CustomSystemPrompt = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawPerKingdomAIConfig(Dictionary<Kingdom, KingdomBrain> brains)
        {
            GUILayout.Label("<b><color=cyan>PER-KINGDOM AI CONFIGURATION</color></b>", _headerStyle);
            GUILayout.Label("<i>Each kingdom can have its own AI model, provider, and personality.</i>", _labelStyle);
            GUILayout.Space(5);

            // Kingdom selector
            var validKingdoms = World.world.kingdoms.list
                .Where(k => k.isAlive() && k.isCiv() && brains.ContainsKey(k))
                .ToList();

            if (validKingdoms.Count == 0)
            {
                GUILayout.Label("<i>No kingdoms available for configuration.</i>", _labelStyle);
                return;
            }

            GUILayout.BeginHorizontal(_panelStyle);
            GUILayout.Label("Select Kingdom:", _labelStyle, GUILayout.Width(120));
            foreach (var k in validKingdoms)
            {
                bool isSelected = _selectedKingdomForConfig == k;
                if (GUILayout.Toggle(isSelected, k.name, _buttonStyle))
                {
                    _selectedKingdomForConfig = k;
                }
            }
            GUILayout.EndHorizontal();

            if (_selectedKingdomForConfig == null || !brains.ContainsKey(_selectedKingdomForConfig))
            {
                GUILayout.Label("<i>Select a kingdom above to edit its AI settings.</i>", _labelStyle);
                return;
            }

            var brain = brains[_selectedKingdomForConfig];
            var config = brain.Config;
            string kName = _selectedKingdomForConfig.name;

            GUILayout.Space(5);
            GUILayout.BeginVertical(_panelStyle);

            GUILayout.Label($"<b><color=yellow>Configuring: {kName}</color></b>", _headerStyle);

            // Provider
            GUILayout.Label("AI Provider:", _labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(config.Provider == Core.AIProvider.Internal, "Internal", _buttonStyle)) config.Provider = Core.AIProvider.Internal;
            if (GUILayout.Toggle(config.Provider == Core.AIProvider.Ollama, "Ollama", _buttonStyle)) config.Provider = Core.AIProvider.Ollama;
            if (GUILayout.Toggle(config.Provider == Core.AIProvider.OpenAI, "OpenAI", _buttonStyle)) config.Provider = Core.AIProvider.OpenAI;
            if (GUILayout.Toggle(config.Provider == Core.AIProvider.Claude, "Claude", _buttonStyle)) config.Provider = Core.AIProvider.Claude;
            GUILayout.EndHorizontal();

            if (config.Provider != Core.AIProvider.Internal)
            {
                GUILayout.Space(5);

                GUILayout.Label("Model Name:", _labelStyle);
                config.Model = GUILayout.TextField(config.Model, GUILayout.Height(25));

                if (config.Provider == Core.AIProvider.Ollama)
                {
                    GUILayout.Label("Endpoint URL:", _labelStyle);
                    config.Endpoint = GUILayout.TextField(config.Endpoint, GUILayout.Height(25));
                }
                else
                {
                    GUILayout.Label("API Key:", _labelStyle);
                    config.ApiKey = GUILayout.PasswordField(config.ApiKey, '*', GUILayout.Height(25));
                }

                GUILayout.Space(5);

                // --- Rate Limiting ---
                GUILayout.Label("<b>Rate Limiting</b>", _labelStyle);
                GUILayout.Label($"Min delay: {config.MinDelayBetweenCalls:F1}s", _labelStyle);
                config.MinDelayBetweenCalls = GUILayout.HorizontalSlider(config.MinDelayBetweenCalls, 0.1f, 10f);
                GUILayout.Label($"Max calls/min: {config.MaxCallsPerMinute}", _labelStyle);
                config.MaxCallsPerMinute = (int)GUILayout.HorizontalSlider(config.MaxCallsPerMinute, 1f, 120f);

                // --- Context Window ---
                GUILayout.Space(5);
                GUILayout.Label("<b>Context Window</b>", _labelStyle);
                GUILayout.Label($"Context: {config.ContextWindowTokens} tokens", _labelStyle);
                string[] ctxOpts2 = new[] { "2048", "4096", "8192", "16384", "32768", "128000" };
                int ctxIdx2 = System.Array.IndexOf(ctxOpts2, config.ContextWindowTokens.ToString());
                if (ctxIdx2 < 0) ctxIdx2 = 1;
                ctxIdx2 = GUILayout.SelectionGrid(ctxIdx2, ctxOpts2, 3, _buttonStyle);
                config.ContextWindowTokens = int.Parse(ctxOpts2[ctxIdx2]);
                GUILayout.Label($"Max response: {config.MaxResponseTokens} tokens", _labelStyle);
                config.MaxResponseTokens = (int)GUILayout.HorizontalSlider(config.MaxResponseTokens, 64f, 2048f);

                // --- Token Budget ---
                GUILayout.Space(5);
                config.EnableTokenBudget = GUILayout.Toggle(config.EnableTokenBudget, "Enable token budget", _buttonStyle);
                if (config.EnableTokenBudget)
                {
                    GUILayout.Label($"Max tokens/min: {config.MaxTokensPerMinute}", _labelStyle);
                    config.MaxTokensPerMinute = (int)GUILayout.HorizontalSlider(config.MaxTokensPerMinute, 1000f, 50000f);
                }

                GUILayout.Space(5);
                GUILayout.Label("<b>Custom System Prompt</b>", _labelStyle);
                GUILayout.Label($"<size=11><i>Personality override for {kName}. Empty = default game personality.</i></size>", _labelStyle);
                config.CustomSystemPrompt = GUILayout.TextArea(config.CustomSystemPrompt, GUILayout.Height(120));

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset to Default", _buttonStyle))
                {
                    config.CustomSystemPrompt = "";
                }
                if (GUILayout.Button("Copy from Global", _buttonStyle))
                {
                    config.CustomSystemPrompt = Core.GlobalSettings.GlobalAI.CustomSystemPrompt;
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<i>Internal AI uses built-in rule engine for this kingdom.</i>", _labelStyle);
            }

            GUILayout.EndVertical();
        }

        // --- TAB: SETTINGS ---
        private void DrawSettings()
        {
            GUILayout.Label("<b>ADVANCED FEATURES</b>", _headerStyle);
            GUILayout.BeginVertical(_panelStyle);
            _enableInterview = GUILayout.Toggle(_enableInterview, "Enable Royal Interview Tab", _buttonStyle);
            _enableMap = GUILayout.Toggle(_enableMap, "Enable Strategic Heatmaps Tab", _buttonStyle);
            _enableDiplomacy = GUILayout.Toggle(_enableDiplomacy, "Enable Diplomacy Web Tab", _buttonStyle);
            
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.IsEnabled = GUILayout.Toggle(NotificationManager.Instance.IsEnabled, "Enable Desktop-style Pop-up Notifications", _buttonStyle);
            }
            GUILayout.EndVertical();
            GUILayout.Space(10);

            GUILayout.Label("<b>APPEARANCE</b>", _headerStyle);
            GUILayout.BeginVertical(_panelStyle);
            GUILayout.Label($"Window Opacity: {_bgOpacity:F2}", _labelStyle);
            _bgOpacity = GUILayout.HorizontalSlider(_bgOpacity, 0.5f, 1f);
            
            GUILayout.Label("Accent Theme Color:", _labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_accentColorIndex == 0, "Blue", _buttonStyle)) _accentColorIndex = 0;
            if (GUILayout.Toggle(_accentColorIndex == 1, "Red", _buttonStyle)) _accentColorIndex = 1;
            if (GUILayout.Toggle(_accentColorIndex == 2, "Green", _buttonStyle)) _accentColorIndex = 2;
            if (GUILayout.Toggle(_accentColorIndex == 3, "Purple", _buttonStyle)) _accentColorIndex = 3;
            if (GUILayout.Toggle(_accentColorIndex == 4, "Gold", _buttonStyle)) _accentColorIndex = 4;
            GUILayout.EndHorizontal();

            GUILayout.Label($"Font Size: {_fontSize}", _labelStyle);
            _fontSize = (int)GUILayout.HorizontalSlider(_fontSize, 10f, 24f);

            _showNewsTicker = GUILayout.Toggle(_showNewsTicker, "Show News Ticker", _buttonStyle);
            _showMapOverlays = GUILayout.Toggle(_showMapOverlays, "Show Map Overlays", _buttonStyle);
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.Label("<b>AI USAGE STATISTICS</b>", _headerStyle);
            GUILayout.BeginVertical(_panelStyle);
            var aiClient = Core.AIProviderClient.Instance;
            if (aiClient != null)
            {
                GUILayout.Label($"Total calls this session: <color=#66ff66>{aiClient.TotalCallsMade}</color>", _labelStyle);
                GUILayout.Label($"Total tokens used (est): <color=#66ff66>{aiClient.TotalTokensUsed:N0}</color>", _labelStyle);
                GUILayout.Label($"Tokens this minute (est): <color=#ffcc66>{aiClient.CurrentTokensPerMinute:N0}</color>", _labelStyle);
                GUILayout.Label($"Calls dropped (rate limit): <color=#ff6666>{aiClient.CallsDropped}</color>", _labelStyle);
                GUILayout.Label($"Pending in queue: <color=#ffcc66>{aiClient.PendingQueueCount}</color>", _labelStyle);
            }
            else
            {
                GUILayout.Label("<i>AI client not initialized.</i>", _labelStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.Label("<b>GLOBAL LANGUAGE</b>", _headerStyle);
            GUILayout.BeginHorizontal(_panelStyle);
            if (GUILayout.Toggle(Core.GlobalSettings.Language == Core.GameLanguage.English, "EN", _buttonStyle)) Core.GlobalSettings.Language = Core.GameLanguage.English;
            if (GUILayout.Toggle(Core.GlobalSettings.Language == Core.GameLanguage.Russian, "RU", _buttonStyle)) Core.GlobalSettings.Language = Core.GameLanguage.Russian;
            if (GUILayout.Toggle(Core.GlobalSettings.Language == Core.GameLanguage.Spanish, "ES", _buttonStyle)) Core.GlobalSettings.Language = Core.GameLanguage.Spanish;
            if (GUILayout.Toggle(Core.GlobalSettings.Language == Core.GameLanguage.Chinese, "ZH", _buttonStyle)) Core.GlobalSettings.Language = Core.GameLanguage.Chinese;
            if (GUILayout.Toggle(Core.GlobalSettings.Language == Core.GameLanguage.German, "DE", _buttonStyle)) Core.GlobalSettings.Language = Core.GameLanguage.German;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label(T("<b>SIMULATION RULES</b>", "<b>ПРАВИЛА СИМУЛЯЦИИ</b>"), _headerStyle);
            GUILayout.BeginVertical(_panelStyle);
            var engine = MainController.Instance.Engine;

            engine.ForceGlobalPeace = GUILayout.Toggle(engine.ForceGlobalPeace, T("Force Global Peace", "Принудительный Глобальный Мир"), _buttonStyle);
            engine.EnableGeographyDetection = GUILayout.Toggle(engine.EnableGeographyDetection, T("Enable Geography Detection", "Включить Определение Географии"), _buttonStyle);
            engine.EnableBiomeAwareness = GUILayout.Toggle(engine.EnableBiomeAwareness, T("Enable Biome Awareness", "Включить Определение Биома"), _buttonStyle);

            GUILayout.Space(10);
            GUILayout.Label(T("<b>AI ADVANCED CONFIG</b>", "<b>РАСШИРЕННЫЕ НАСТРОЙКИ ИИ</b>"), _headerStyle);
            
            GUILayout.Label(T($"AI Update Interval (seconds): {engine.UpdateInterval:F1}", $"Интервал обновления ИИ (сек): {engine.UpdateInterval:F1}"), _labelStyle);
            engine.UpdateInterval = GUILayout.HorizontalSlider(engine.UpdateInterval, 1f, 30f);
            
            GUILayout.Label(T($"Max Chat History: {_maxChatHistory}", $"Макс. История Чата: {_maxChatHistory}"), _labelStyle);
            _maxChatHistory = (int)GUILayout.HorizontalSlider(_maxChatHistory, 10f, 500f);
            
            if (Core.AIProviderClient.Instance != null)
            {
                Core.AIProviderClient.Instance.IsEnabled = GUILayout.Toggle(Core.AIProviderClient.Instance.IsEnabled, "Enable AI Control (Global Master Switch)", _buttonStyle);
            }
            GUILayout.EndVertical();
        }

        // --- TAB: ABOUT ---
        private void DrawAbout()
        {
            GUILayout.BeginVertical(_panelStyle);
            GUILayout.Label("<b><size=20>AIBoxInternal</size></b>", _labelStyle);
            GUILayout.Label("<b>Grand Strategy Engine & Dashboard</b>", _labelStyle);
            GUILayout.Label("Version: 3.0.0 (Real Economy Edition)", _labelStyle);
            GUILayout.Space(10);
            GUILayout.Label("<b>Hotkeys:</b>", _labelStyle);
            GUILayout.Label(" - [Insert]: Show/Hide Menu", _labelStyle);
            GUILayout.Space(10);
            GUILayout.Label("<b>Features:</b>", _labelStyle);
            GUILayout.Label(" - Global AI Setup: One model + prompt for ALL kingdoms, or per-kingdom agents.", _labelStyle);
            GUILayout.Label(" - Diplomatic Mail: Persistent inter-kingdom messaging with conversation threads.", _labelStyle);
            GUILayout.Label(" - Global Logs: Real-time feed of AI decisions.", _labelStyle);
            GUILayout.Label(" - Economy: Real resource trend tracking per kingdom.", _labelStyle);
            GUILayout.Label(" - Data Hub: Sortable stats, happiness, and city health.", _labelStyle);
            GUILayout.Label(" - Military: Global DEFCON meter and active war data.", _labelStyle);
            GUILayout.Label(" - Interview: Talk to Kingdom AI directly via chat.", _labelStyle);
            GUILayout.Label(" - Heatmap: Visual map overlay of tension and wealth.", _labelStyle);
            GUILayout.Label(" - Diplomacy: Node graph of global politics.", _labelStyle);
            GUILayout.Label(" - Real Game Mechanics: AI cannot cheat resources.", _labelStyle);
            GUILayout.EndVertical();
        }

        private Dictionary<string, bool> _headerStates = new Dictionary<string, bool>();
        private bool CollapsingHeader(string label)
        {
            if (!_headerStates.ContainsKey(label)) _headerStates[label] = true;

            if (GUILayout.Button((_headerStates[label] ? "[-] " : "[+] ") + label, _headerStyle))
            {
                _headerStates[label] = !_headerStates[label];
            }

            return _headerStates[label];
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}
