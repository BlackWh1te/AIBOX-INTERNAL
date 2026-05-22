using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIBoxInternal.Core
{
    [Serializable]
    public class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream = false;
    }

    [Serializable]
    public class OllamaResponse
    {
        public string response;
    }

    [Serializable]
    public class OpenAIRequest
    {
        public string model;
        public int max_tokens;
        public List<OpenAIMessage> messages;
    }

    [Serializable]
    public class OpenAIMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class OpenAIResponse
    {
        public List<OpenAIChoice> choices;
    }

    [Serializable]
    public class OpenAIChoice
    {
        public OpenAIMessage message;
    }

    [Serializable]
    public class ClaudeRequest
    {
        public string model;
        public int max_tokens = 1024;
        public List<OpenAIMessage> messages;
    }

    [Serializable]
    public class ClaudeResponse
    {
        public List<ClaudeContentBlock> content;
    }

    [Serializable]
    public class ClaudeContentBlock
    {
        public string type;
        public string text;
    }

    public class AIProviderClient : MonoBehaviour
    {
        public static AIProviderClient Instance { get; private set; }
        
        public bool IsEnabled = true;

        // ==================== RATE LIMITING & TOKEN TRACKING ====================
        private Queue<(float timestamp, int estimatedTokens)> _callHistory = new Queue<(float, int)>();
        private float _lastGlobalCallTime = -999f;
        private Dictionary<string, float> _kingdomLastCallTime = new Dictionary<string, float>();
        private Queue<(Kingdom kingdom, KingdomBrain brain, string prompt, Action<string> callback, KingdomConfig config)> _pendingRequests = new Queue<(Kingdom, KingdomBrain, string, Action<string>, KingdomConfig)>();
        private bool _isProcessingQueue = false;

        /// <summary>
        /// Estimated tokens used this minute (sliding window).
        /// </summary>
        public int CurrentTokensPerMinute { get; private set; }
        /// <summary>
        /// Estimated tokens used in the last session total.
        /// </summary>
        public long TotalTokensUsed { get; private set; }
        /// <summary>
        /// Total AI calls made this session.
        /// </summary>
        public int TotalCallsMade { get; private set; }
        /// <summary>
        /// Calls dropped due to rate limits this session.
        /// </summary>
        public int CallsDropped { get; private set; }
        /// <summary>
        /// Number of requests currently queued waiting for rate limits.
        /// </summary>
        public int PendingQueueCount => _pendingRequests?.Count ?? 0;

        void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Rough token estimate: ~4 characters per token for English/Russian.
        /// Fast enough to run every frame if needed.
        /// </summary>
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length / 4;
        }

        /// <summary>
        /// Check if an AI call is allowed right now based on rate limits.
        /// Returns true if call should proceed, false if it should be queued or dropped.
        /// </summary>
        private bool CheckRateLimit(KingdomConfig config, out string blockReason)
        {
            blockReason = "";
            float now = Time.time;

            // 1. Minimum delay between ANY calls (global)
            if (now - _lastGlobalCallTime < config.MinDelayBetweenCalls)
            {
                blockReason = $"Global cooldown: {(config.MinDelayBetweenCalls - (now - _lastGlobalCallTime)):F1}s remaining";
                return false;
            }

            // 2. Max calls per minute (sliding window)
            float oneMinuteAgo = now - 60f;
            while (_callHistory.Count > 0 && _callHistory.Peek().timestamp < oneMinuteAgo)
                _callHistory.Dequeue();

            if (_callHistory.Count >= config.MaxCallsPerMinute)
            {
                blockReason = $"Rate limit: {_callHistory.Count}/{config.MaxCallsPerMinute} calls this minute";
                return false;
            }

            // 3. Token budget per minute
            if (config.EnableTokenBudget)
            {
                int tokensThisMinute = 0;
                foreach (var c in _callHistory) tokensThisMinute += c.estimatedTokens;
                if (tokensThisMinute >= config.MaxTokensPerMinute)
                {
                    blockReason = $"Token budget exhausted: {tokensThisMinute}/{config.MaxTokensPerMinute} tokens this minute";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Record a successful AI call for rate limit tracking.
        /// </summary>
        private void RecordCall(string kingdomName, string prompt, KingdomConfig config)
        {
            float now = Time.time;
            int estimatedTokens = EstimateTokens(prompt) + config.MaxResponseTokens;

            _callHistory.Enqueue((now, estimatedTokens));
            _lastGlobalCallTime = now;
            _kingdomLastCallTime[kingdomName] = now;
            TotalTokensUsed += estimatedTokens;
            TotalCallsMade++;
            int tpm = 0;
            foreach (var c in _callHistory) tpm += c.estimatedTokens;
            CurrentTokensPerMinute = tpm;
        }

        /// <summary>
        /// Check if a specific kingdom is on cooldown.
        /// </summary>
        public bool IsKingdomOnCooldown(string kingdomName, float minDelay)
        {
            if (!_kingdomLastCallTime.ContainsKey(kingdomName)) return false;
            return Time.time - _kingdomLastCallTime[kingdomName] < minDelay;
        }

        /// <summary>
        /// Get estimated time until a kingdom can call AI again (seconds).
        /// </summary>
        public float GetKingdomCooldownRemaining(string kingdomName, float minDelay)
        {
            if (!_kingdomLastCallTime.ContainsKey(kingdomName)) return 0f;
            float remaining = minDelay - (Time.time - _kingdomLastCallTime[kingdomName]);
            return Mathf.Max(0f, remaining);
        }

        // ==================== END RATE LIMITING ====================

        private string GetLanguagePromptModifier(GameLanguage lang)
        {
            switch (lang)
            {
                case GameLanguage.Russian:
                    return "ОЧЕНЬ ВАЖНО: Вы ДОЛЖНЫ отвечать полностью на русском языке. Все ваши мысли и действия должны быть написаны по-русски.";
                case GameLanguage.Spanish:
                    return "MUY IMPORTANTE: DEBES responder completamente en español. Todos tus pensamientos y acciones deben estar en español.";
                case GameLanguage.Chinese:
                    return "非常重要：你必须完全用中文回答。你所有的想法和行动都必须用中文写。";
                case GameLanguage.German:
                    return "SEHR WICHTIG: Du MUSST vollständig auf Deutsch antworten. Alle deine Gedanken und Handlungen müssen auf Deutsch geschrieben sein.";
                case GameLanguage.English:
                default:
                    return "VERY IMPORTANT: You MUST respond entirely in English. All your thoughts and actions must be in English.";
            }
        }

        public string GetSystemPrompt(Kingdom k, KingdomBrain brain)
        {
            string langModifier = GetLanguagePromptModifier(GlobalSettings.Language);
            string lore = string.Join("\n", brain.LoreHistory);
            string luxuries = string.Join(", ", brain.ControlledLuxuries);

            // Determine which custom prompt to use: global (if enabled) or per-kingdom
            string customPrompt = GlobalSettings.UseGlobalAI && !string.IsNullOrEmpty(GlobalSettings.GlobalAI.CustomSystemPrompt)
                ? GlobalSettings.GlobalAI.CustomSystemPrompt
                : brain.Config.CustomSystemPrompt;

            // If user set a custom personality prompt, use it as the role — but ALWAYS append game rules and real in-game data.
            string personalityBlock = !string.IsNullOrEmpty(customPrompt)
                ? customPrompt
                : $"You are the King of {k.name}. Your personality is {brain.Personality} with ambition level {brain.Ambition:F1}/1.0. Strategy: High World Tension means you should focus on military. Priority: Conquer cities with luxuries you don't have.";

            string mechanicsGuide = GameMechanicsResearch.GetMechanicsGuide();
            string trendReport = brain.MemoryBank != null ? brain.MemoryBank.GetFullTrendReport(k) : "";

            return $"{personalityBlock}\n\n" +
                   $"=== CRITICAL ANTI-HALLUCINATION RULES ===\n" +
                   $"1. You are playing a SIMULATION GAME called WorldBox. You are NOT the real Russia, USA, or any real country.\n" +
                   $"2. DO NOT invent real-world statistics, geography, or populations. Use ONLY the exact numbers from your in-game Context.\n" +
                   $"3. ALWAYS use the exact population and city count from your Context. Do not say 'millions' if Context says 50.\n" +
                   $"4. Base ALL your decisions on the in-game data provided below.\n" +
                   $"5. YOU CANNOT DIRECTLY CREATE RESOURCES OR BUILD BUILDINGS. Citizens do this automatically. You are a STRATEGIST, not a god.\n" +
                   $"6. Every strategic action that costs gold DEDUCTS from your REAL treasury. If you don't have enough gold, the action fails.\n" +
                   $"7. Before spending gold, check if you can afford it. Consider your economic trends.\n" +
                   $"8. SHARED CONTROL: Your kingdom also has native AI (citizens, city leaders, king plots). They act independently.\n" +
                   $"   You may order peace, but a rebellion war can still start. You may order alliance, but your king may plot war.\n" +
                   $"   React to the world as it is NOW — not as you last left it.\n" +
                   $"=========================================\n\n" +
                   $"{mechanicsGuide}\n\n" +
                    $"{langModifier}\n" +
                    $"Lore History:\n{lore}\n" +
                    $"World Tension: {GlobalState.WorldTension:P0} ({GlobalState.CurrentPhase})\n" +
                    $"Controlled Luxuries: {luxuries}\n" +
                    $"Active Spies: {brain.SpiesActive} | Active Missions: {brain.ActiveMissions.Count}\n\n" +
                    $"{trendReport}\n\n" +
                   $"Describe your thoughts and then your action.\n" +
                   $"=== EVENT TRACKING (Your Morning Inbox) ===\n" +
                   $"At the top of every turn, you will see an 'EVENTS SINCE YOUR LAST TURN' section. This is your morning briefing — check it first.\n" +
                   $"Three types of events appear:\n" +
                   $"  [YOU] — Actions you ordered and their results (SUCCEEDED or FAILED).\n" +
                   $"  [GAME] — Native game events: wars starting, succession, rebellions, city loss/gain.\n" +
                   $"  [FROM KingdomName] — Actions by OTHER kingdoms directed AT you: mail received, war declarations, alliance invites.\n" +
                   $"You ONLY see events directed at you — you cannot spy on other kingdoms' internal actions. That would be cheating.\n" +
                   $"If you ordered an action and it shows FAILED, check why before trying again.\n" +
                   $"==============================\n\n" +
                   $"=== DIPLOMATIC MAIL SYSTEM ===\n" +
                   $"You can send and receive persistent diplomatic mail to/from other kingdoms. Messages survive across cycles and form conversation threads.\n" +
                   $"When you receive mail, you will see it in your Context under 'INCOMING DIPLOMATIC CORRESPONDENCE' and also as a [FROM KingdomName] event.\n" +
                   $"You should read mail and respond if appropriate.\n" +
                   $"Mail subjects affect diplomatic opinion: insults/threats lower it (-5), praise/gifts raise it (+5).\n" +
                   $"To send mail: SEND_MAIL: KingdomName ~ Subject ~ Body\n" +
                   $"  Example: SEND_MAIL: Orcs ~ Peace Offer ~ We propose a 10-year peace treaty. Will you accept?\n" +
                   $"  Use ~ (tilde) as the separator. Do NOT use | inside the mail body.\n" +
                   $"==============================\n\n" +
                   $"=== INTRIGUE / PLOT SYSTEM ===\n" +
                   $"Your king can start plots (schemes) that progress over time and trigger effects when complete.\n" +
                   $"Plots require: king level, stats (intelligence/diplomacy/warfare/stewardship), renown, and gold.\n" +
                   $"You will see your king's stats and any active plots in the Context.\n" +
                   $"Available plot types: new_war, alliance_create, rebellion, cause_rebellion\n" +
                   $"To start a plot: START_PLOT: plotType ~ TargetName\n" +
                   $"  Example: START_PLOT: new_war ~ Orcs\n" +
                   $"  Example: START_PLOT: alliance_create ~ Elves\n" +
                   $"  Example: START_PLOT: cause_rebellion ~ OrcCity\n" +
                   $"  Note: The king must be free (not already in a plot) and meet requirements.\n" +
                   $"==============================\n\n" +
                   $"=== CITY-LEVEL MANAGEMENT ===\n" +
                   $"You can micromanage individual cities. Citizens handle production, but you set priorities and conscript troops.\n" +
                   $"SET_CITY_PRIORITY: CityName ~ priority — Set a city's focus: Balanced, Military, Economy, Growth, FoodFirst, Housing.\n" +
                   $"  Example: SET_CITY_PRIORITY: Capital ~ Military\n" +
                   $"CONSCRIPT: CityName ~ count — Convert citizens to warriors in a specific city (requires available citizens and gold).\n" +
                   $"  Example: CONSCRIPT: Capital ~ 5\n" +
                   $"FORCE_CITY_CHECK: CityName — Force a city to process its turn immediately (can accelerate growth/building).\n" +
                   $"  Example: FORCE_CITY_CHECK: Capital\n" +
                   $"==============================\n\n" +
                   $"=== ENHANCED ESPIONAGE ===\n" +
                   $"Beyond basic spying, you can order specialized espionage missions:\n" +
                   $"STEAL_GOLD: KingdomName — Spy infiltrates treasury and extracts gold intel (not actual gold, but economic data).\n" +
                   $"SABOTAGE_BUILDINGS: KingdomName — Spy attempts to destroy up to 3 buildings in the target capital.\n" +
                   $"These require an active spy mission (use RECRUIT_SPY or GATHER_INTEL first).\n" +
                   $"==============================\n\n" +
                   $"=== MULTI-TURN PLANNING ===\n" +
                   $"You can create strategic plans that span multiple turns. The system tracks progress and auto-completes when goals are met.\n" +
                   $"CREATE_PLAN: PlanName ~ TargetKingdom ~ TargetCity ~ DurationTurns\n" +
                   $"  PlanName options: CONQUER, DEFEND, ECONOMY_BUILDUP, DIPLOMACY_PUSH\n" +
                   $"  Example: CREATE_PLAN: CONQUER ~ Orcs ~ OrcCapital ~ 8\n" +
                   $"  The plan auto-completes if: target is destroyed, target city captured, or peace restored (DEFEND plans).\n" +
                   $"  Plans expire after DurationTurns if not completed.\n" +
                   $"CANCEL_PLAN — Abandon your current plan.\n" +
                   $"==============================\n\n" +
                   $"=== COMBAT TACTICS ===\n" +
                   $"During active wars, you can issue tactical orders to your armies:\n" +
                   $"SET_WAR_STANCE:AGGRESSIVE / BALANCED / DEFENSIVE — Set your kingdom's combat posture.\n" +
                   $"SIEGE_CITY — All warriors march toward the nearest enemy city and attempt to siege it.\n" +
                   $"SALLY — Warriors charge from defensive positions toward the nearest enemy city.\n" +
                   $"RETREAT — Pull all warriors back to the capital. Sets stance to Retreat.\n" +
                   $"==============================\n\n" +
                   $"Available Actions: WAR, PEACE, FESTIVAL, SURVEY_LAND, PLAN_CITY, PLAN_CITY: SiteName, RECRUIT_SPY, " +
                   $"GATHER_INTEL: KingdomName, START_TRADE: KingdomName, SEND_MAIL: KingdomName ~ Subject ~ Body, " +
                   $"FORM_ALLIANCE: KingdomName, LEAVE_ALLIANCE, OFFER_PEACE: KingdomName, BRIBE_CITY: CityName, " +
                   $"ASSASSINATE_CHIEF: KingdomName, PRAY_FOR_MIRACLE, DECLARE_WAR, ASSASSINATE, SABOTAGE, " +
                   $"HIRE_MERCENARIES, STRENGTHEN_CULTURE, START_PLOT: plotType ~ TargetName, " +
                   $"ADVOCATE_LAW: law_name ~ enable/disable, " +
                   $"SET_TAX: LOW/MEDIUM/HIGH, SET_BUDGET: DEFENSIVE/BALANCED/AGGRESSIVE, " +
                   $"SET_STANCE: BALANCED/BLITZKRIEG/GUERRILLA/SCORCHED_EARTH, " +
                   $"SET_FOCUS: EXPANSION/ECONOMIC/CULTURAL/MILITARY, " +
                   $"SET_CITY_PRIORITY: CityName ~ priority, CONSCRIPT: CityName ~ count, FORCE_CITY_CHECK: CityName, " +
                   $"STEAL_GOLD: KingdomName, SABOTAGE_BUILDINGS: KingdomName, " +
                   $"CREATE_PLAN: PlanName ~ TargetKingdom ~ TargetCity ~ Turns, CANCEL_PLAN, " +
                   $"SET_WAR_STANCE:AGGRESSIVE, SET_WAR_STANCE:BALANCED, SET_WAR_STANCE:DEFENSIVE, " +
                   $"SIEGE_CITY, SALLY, RETREAT.\n" +
                   $"Format: THOUGHT: <reasoning based on in-game data> | ACTION: <one action>\n\n" +
                   $"=== SMART TARGETING ===\n" +
                   $"When you declare war or conduct espionage, the system automatically picks the BEST target based on:\n" +
                   $"  - Diplomatic opinion (hostile kingdoms prioritized)\n" +
                   $"  - Military strength (avoids much stronger enemies)\n" +
                   $"  - Proximity (prefers nearby kingdoms)\n" +
                   $"  - Multi-front penalty (avoids war if already fighting)\n" +
                   $"  - Alliance status (never targets allies)\n" +
                   $"You can trust the targeting system, but specify a target explicitly if you have a specific enemy in mind.";
        }

        public void Ask(Kingdom k, KingdomBrain brain, string prompt, Action<string> callback)
        {
            if (!IsEnabled) return;

            KingdomConfig config = GlobalSettings.UseGlobalAI ? GlobalSettings.GlobalAI.ToKingdomConfig() : brain.Config;

            // Apply context window truncation BEFORE rate limit check
            int systemTokens = EstimateTokens(GetSystemPrompt(k, brain));
            int availableContextTokens = config.ContextWindowTokens - systemTokens - config.MaxResponseTokens - 100; // 100 token safety margin
            string truncatedPrompt = TruncatePromptToTokenBudget(prompt, availableContextTokens);

            // Check per-kingdom cooldown
            if (IsKingdomOnCooldown(k.name, config.MinDelayBetweenCalls))
            {
                float remaining = GetKingdomCooldownRemaining(k.name, config.MinDelayBetweenCalls);
                Debug.Log($"[AIBox] Rate limit: {k.name} on cooldown ({remaining:F1}s remaining). Queuing request.");
                _pendingRequests.Enqueue((k, brain, truncatedPrompt, callback, config));
                if (!_isProcessingQueue)
                    StartCoroutine(ProcessRequestQueue());
                return;
            }

            // Check global rate limits
            if (!CheckRateLimit(config, out string blockReason))
            {
                Debug.Log($"[AIBox] Rate limit: {blockReason}. Queuing request for {k.name}.");
                _pendingRequests.Enqueue((k, brain, truncatedPrompt, callback, config));
                if (!_isProcessingQueue)
                    StartCoroutine(ProcessRequestQueue());
                return;
            }

            // Execute immediately
            ExecuteRequest(k, brain, truncatedPrompt, callback, config);
        }

        private void ExecuteRequest(Kingdom k, KingdomBrain brain, string prompt, Action<string> callback, KingdomConfig config)
        {
            RecordCall(k.name, prompt, config);

            switch (config.Provider)
            {
                case AIProvider.Ollama:
                    StartCoroutine(PostOllama(config, prompt, callback));
                    break;
                case AIProvider.OpenAI:
                    StartCoroutine(PostOpenAI(config, prompt, callback));
                    break;
                case AIProvider.Claude:
                    StartCoroutine(PostClaude(config, prompt, callback));
                    break;
                default:
                    break;
            }
        }

        private IEnumerator ProcessRequestQueue()
        {
            _isProcessingQueue = true;
            while (_pendingRequests.Count > 0)
            {
                var req = _pendingRequests.Peek();
                KingdomConfig config = req.config;

                // Wait until this kingdom's cooldown expires
                if (IsKingdomOnCooldown(req.kingdom.name, config.MinDelayBetweenCalls))
                {
                    float wait = GetKingdomCooldownRemaining(req.kingdom.name, config.MinDelayBetweenCalls);
                    yield return new WaitForSeconds(wait + 0.1f);
                    continue;
                }

                // Check global rate limits
                if (!CheckRateLimit(config, out string _))
                {
                    yield return new WaitForSeconds(config.MinDelayBetweenCalls);
                    continue;
                }

                // Dequeue and execute
                _pendingRequests.Dequeue();
                ExecuteRequest(req.kingdom, req.brain, req.prompt, req.callback, req.config);

                // Small delay between dequeued requests
                yield return new WaitForSeconds(config.MinDelayBetweenCalls);
            }
            _isProcessingQueue = false;
        }

        /// <summary>
        /// Truncate a prompt to fit within a token budget by progressively removing less-critical sections.
        /// </summary>
        private string TruncatePromptToTokenBudget(string prompt, int maxTokens)
        {
            if (EstimateTokens(prompt) <= maxTokens) return prompt;

            // Strategy: progressively strip verbose sections in order of least importance
            // 1. Trim action history (oldest first)
            string working = prompt;
            int attempts = 0;
            while (EstimateTokens(working) > maxTokens && attempts < 5)
            {
                attempts++;

                // Try removing recent action history section
                int actionIdx = working.LastIndexOf("\nRECENT ACTION HISTORY");
                if (actionIdx > 0)
                {
                    working = working.Substring(0, actionIdx);
                    continue;
                }

                // Try removing trends section
                int trendsIdx = working.LastIndexOf("\nTRENDS (from memory)");
                if (trendsIdx > 0)
                {
                    working = working.Substring(0, trendsIdx);
                    continue;
                }

                // Try removing diplomacy details (keep summary)
                int diploIdx = working.LastIndexOf("\nDIPLOMACY:");
                if (diploIdx > 0)
                {
                    int endIdx = working.IndexOf("\n\n", diploIdx + 1);
                    if (endIdx > 0) working = working.Substring(0, diploIdx) + working.Substring(endIdx);
                    else working = working.Substring(0, diploIdx);
                    continue;
                }

                // Try removing building summary
                int bldgIdx = working.IndexOf("Buildings: Houses=");
                if (bldgIdx > 0)
                {
                    int endIdx = working.IndexOf("\n", bldgIdx + 1);
                    if (endIdx > 0) working = working.Substring(0, bldgIdx) + working.Substring(endIdx);
                    else working = working.Substring(0, bldgIdx);
                    continue;
                }

                // Try removing city resource details (keep basic city list)
                int cityDetailIdx = working.IndexOf("Resources:");
                while (cityDetailIdx > 0 && EstimateTokens(working) > maxTokens)
                {
                    int endLine = working.IndexOf("\n", cityDetailIdx);
                    if (endLine > 0)
                        working = working.Substring(0, cityDetailIdx) + working.Substring(endLine + 1);
                    else
                        break;
                    cityDetailIdx = working.IndexOf("Resources:", cityDetailIdx);
                }

                // Last resort: hard truncate with a warning
                if (EstimateTokens(working) > maxTokens)
                {
                    int maxChars = maxTokens * 4;
                    if (working.Length > maxChars)
                    {
                        working = working.Substring(0, maxChars) + "\n[CONTEXT TRUNCATED DUE TO TOKEN LIMIT]";
                    }
                    break;
                }
            }

            return working;
        }

        private IEnumerator PostOllama(KingdomConfig config, string prompt, Action<string> callback)
        {
            OllamaRequest req = new OllamaRequest { model = config.Model, prompt = prompt };
            string json = JsonUtility.ToJson(req);

            using (UnityWebRequest www = new UnityWebRequest(config.Endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try {
                        OllamaResponse res = JsonUtility.FromJson<OllamaResponse>(www.downloadHandler.text);
                        callback?.Invoke(res.response);
                    } catch (Exception e) {
                        Debug.LogWarning($"[AIBox] Ollama response parse error: {e.Message}");
                        callback?.Invoke("THOUGHT: Error parsing Ollama response. | ACTION: STAY_STILL");
                    }
                }
                else
                {
                    callback?.Invoke("THOUGHT: Ollama connection failed. | ACTION: STAY_STILL");
                }
            }
        }

        private IEnumerator PostOpenAI(KingdomConfig config, string prompt, Action<string> callback)
        {
            OpenAIRequest req = new OpenAIRequest {
                model = config.Model,
                max_tokens = config.MaxResponseTokens,
                messages = new List<OpenAIMessage> {
                    new OpenAIMessage { role = "user", content = prompt }
                }
            };
            string json = JsonUtility.ToJson(req);

            using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + config.ApiKey);

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try {
                        OpenAIResponse res = JsonUtility.FromJson<OpenAIResponse>(www.downloadHandler.text);
                        callback?.Invoke(res.choices[0].message.content);
                    } catch (Exception e) {
                        Debug.LogWarning($"[AIBox] OpenAI response parse error: {e.Message}");
                        callback?.Invoke("THOUGHT: Error parsing OpenAI response. | ACTION: STAY_STILL");
                    }
                }
                else
                {
                    callback?.Invoke("THOUGHT: OpenAI connection failed. | ACTION: STAY_STILL");
                }
            }
        }

        private IEnumerator PostClaude(KingdomConfig config, string prompt, Action<string> callback)
        {
            ClaudeRequest req = new ClaudeRequest {
                model = config.Model,
                max_tokens = config.MaxResponseTokens,
                messages = new List<OpenAIMessage> {
                    new OpenAIMessage { role = "user", content = prompt }
                }
            };
            string json = JsonUtility.ToJson(req);

            using (UnityWebRequest www = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("x-api-key", config.ApiKey);
                www.SetRequestHeader("anthropic-version", "2023-06-01");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try {
                        ClaudeResponse res = JsonUtility.FromJson<ClaudeResponse>(www.downloadHandler.text);
                        if (res.content != null && res.content.Count > 0) {
                            string text = "";
                            foreach (var block in res.content) {
                                if (block.type == "text" && !string.IsNullOrEmpty(block.text)) text += block.text;
                            }
                            callback?.Invoke(text);
                        } else {
                            callback?.Invoke("THOUGHT: Claude returned empty content. | ACTION: STAY_STILL");
                        }
                    } catch (Exception e) {
                        Debug.LogWarning($"[AIBox] Claude response parse error: {e.Message}");
                        callback?.Invoke("THOUGHT: Error parsing Claude response. | ACTION: STAY_STILL");
                    }
                }
                else
                {
                    callback?.Invoke("THOUGHT: Claude connection failed. | ACTION: STAY_STILL");
                }
            }
        }

        public void SendChatMessage(Kingdom k, KingdomBrain brain, string userMessage, Action<string> onComplete)
        {
            KingdomConfig chatConfig = GlobalSettings.UseGlobalAI ? GlobalSettings.GlobalAI.ToKingdomConfig() : brain.Config;
            if (chatConfig.Provider == AIProvider.Internal)
            {
                onComplete?.Invoke("I am an internal AI. I cannot chat.");
                return;
            }

            StartCoroutine(SendChatMessageRoutine(k, brain, userMessage, onComplete, chatConfig));
        }

        private IEnumerator SendChatMessageRoutine(Kingdom k, KingdomBrain brain, string userMessage, Action<string> onComplete, KingdomConfig config)
        {
            brain.ChatHistory.Add($"You: {userMessage}");
            string langModifier = GetLanguagePromptModifier(GlobalSettings.Language);
            string systemPrompt = $"You are King {k.name}. {langModifier} This is an interview. Respond naturally to the user. Do NOT use the THOUGHT/ACTION format. Just talk. IMPORTANT: You are a king in a simulation game, not a real country. Base your answers only on your in-game situation.";
            
            string history = string.Join("\n", brain.ChatHistory);
            string fullPrompt = $"{systemPrompt}\n\n{history}\nKing {k.name}:";

            if (config.Provider == AIProvider.Ollama)
            {
                OllamaRequest req = new OllamaRequest { model = config.Model, prompt = fullPrompt };
                string jsonReq = JsonUtility.ToJson(req);
                using (UnityWebRequest www = new UnityWebRequest($"{config.Endpoint}/api/generate", "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonReq);
                    www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    www.downloadHandler = new DownloadHandlerBuffer();
                    www.SetRequestHeader("Content-Type", "application/json");

                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        var res = JsonUtility.FromJson<OllamaResponse>(www.downloadHandler.text);
                        brain.ChatHistory.Add($"King {k.name}: {res.response}");
                        onComplete?.Invoke(res.response);
                    }
                    else
                    {
                        onComplete?.Invoke("Error connecting to AI.");
                    }
                }
            }
            else
            {
                string chatLangMod = GetLanguagePromptModifier(GlobalSettings.Language);
                string chatSystem = $"You are King {k.name}. {chatLangMod} This is an interview. Respond naturally. IMPORTANT: You are a king in a simulation game, not a real country.";
                string chatContext = RealTimeDB.BuildContextString(k, brain);
                string chatPrompt = $"{chatSystem}\n\n{chatContext}\n\nHistory:\n{string.Join("\n", brain.ChatHistory)}\nKing {k.name}:";

                if (config.Provider == AIProvider.OpenAI)
                {
                    OpenAIRequest req = new OpenAIRequest {
                        model = config.Model,
                        messages = new List<OpenAIMessage> {
                            new OpenAIMessage { role = "system", content = chatSystem },
                            new OpenAIMessage { role = "user", content = chatPrompt }
                        }
                    };
                    string jsonReq = JsonUtility.ToJson(req);
                    StartCoroutine(SendOpenAIChat(jsonReq, config, k, onComplete));
                }
                else if (config.Provider == AIProvider.Claude)
                {
                    ClaudeRequest req = new ClaudeRequest {
                        model = config.Model,
                        max_tokens = 512,
                        messages = new List<OpenAIMessage> {
                            new OpenAIMessage { role = "user", content = chatPrompt }
                        }
                    };
                    string jsonReq = JsonUtility.ToJson(req);
                    StartCoroutine(SendClaudeChat(jsonReq, config, k, brain, onComplete));
                }
                else
                {
                    onComplete?.Invoke("Chat not supported for this provider.");
                }
            }
        }

        private IEnumerator SendOpenAIChat(string json, KingdomConfig config, Kingdom k, Action<string> onComplete)
        {
            using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + config.ApiKey);

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try {
                        OpenAIResponse res = JsonUtility.FromJson<OpenAIResponse>(www.downloadHandler.text);
                        string text = res.choices[0].message.content;
                        if (MainController.Instance.Engine.GetBrains().TryGetValue(k, out var brain))
                            brain.ChatHistory.Add($"King {k.name}: {text}");
                        onComplete?.Invoke(text);
                    } catch (Exception e) {
                        Debug.LogWarning($"[AIBox] GPT chat parse error: {e.Message}");
                        onComplete?.Invoke("Error parsing GPT chat response.");
                    }
                }
                else
                {
                    onComplete?.Invoke("GPT chat connection failed.");
                }
            }
        }

        private IEnumerator SendClaudeChat(string json, KingdomConfig config, Kingdom k, KingdomBrain brain, Action<string> onComplete)
        {
            using (UnityWebRequest www = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("x-api-key", config.ApiKey);
                www.SetRequestHeader("anthropic-version", "2023-06-01");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try {
                        ClaudeResponse res = JsonUtility.FromJson<ClaudeResponse>(www.downloadHandler.text);
                        string text = "";
                        if (res.content != null)
                            foreach (var block in res.content)
                                if (block.type == "text" && !string.IsNullOrEmpty(block.text)) text += block.text;
                        brain.ChatHistory.Add($"King {k.name}: {text}");
                        onComplete?.Invoke(text);
                    } catch (Exception e) {
                        Debug.LogWarning($"[AIBox] Claude chat parse error: {e.Message}");
                        onComplete?.Invoke("Error parsing Claude chat response.");
                    }
                }
                else
                {
                    onComplete?.Invoke("Claude chat connection failed.");
                }
            }
        }
    }
}
