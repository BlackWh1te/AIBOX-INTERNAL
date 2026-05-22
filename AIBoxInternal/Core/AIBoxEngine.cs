using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    public class AIBoxEngine
    {
        private float _lastUpdate = 0;
        public float UpdateInterval = 5.0f;
        public bool ForceGlobalPeace = false;
        public bool EnableBiomeAwareness = false;
        public bool EnableGeographyDetection = false;

        private Dictionary<Kingdom, KingdomBrain> _brains = new Dictionary<Kingdom, KingdomBrain>();

        // World change detection: if _brains has entries but none match current world, we loaded a new world
        private int _lastKnownWorldHash = 0;

        /// <summary>
        /// Detects if the player loaded a new world. If so, clears all AI state to prevent
        /// stale Kingdom references from leaking memory and corrupting the new game.
        /// </summary>
        private void DetectAndHandleWorldChange()
        {
            if (World.world == null)
            {
                _lastKnownWorldHash = 0;
                return;
            }

            int currentHash = 0;
            foreach (var k in World.world.kingdoms.list)
            {
                if (k != null && k.isAlive())
                    currentHash = currentHash * 31 + k.name.GetHashCode();
            }

            if (_lastKnownWorldHash != 0 && currentHash != _lastKnownWorldHash)
            {
                // World changed — purge everything
                Debug.Log("[AIBox] World change detected. Purging all AI state...");
                _brains.Clear();
                RealTimeDB.Kingdoms.Clear();
                MailRegistry.Reset();
                GlobalState.PendingWhispers.Clear();
                GlobalState.GlobalNews.Clear();
                GlobalState.WorldTension = 0f;
                GlobalState.CurrentPhase = WorldPhase.Stable;
                // Event trackers are per-brain, so they get cleared with _brains.Clear()
            }

            _lastKnownWorldHash = currentHash;
        }

        // Helper to scan surroundings
        private string GetGeographicContext(Kingdom k)
        {
            if (!EnableGeographyDetection || k.capital == null) return "";

            string report = "";
            WorldTile capTile = k.capital.getTile();
            if (capTile != null)
            {
                string biomeId = capTile.getBiome()?.id ?? "unknown";
                report += $"Geography: Capital situated in {biomeId} biome. ";

                if (EnableBiomeAwareness)
                {
                    string biomeSummary = "";
                    Dictionary<string, int> biomeCount = new Dictionary<string, int>();
                    foreach (City c in k.cities)
                    {
                        WorldTile t = c.getTile();
                        if (t == null) continue;
                        string b = t.getBiome()?.id ?? "unknown";
                        if (!biomeCount.ContainsKey(b)) biomeCount[b] = 0;
                        biomeCount[b]++;
                    }
                    foreach (var kvp in biomeCount)
                        biomeSummary += $"{kvp.Key}({kvp.Value} cities) ";

                    if (!string.IsNullOrEmpty(biomeSummary))
                        report += $"Territory spans biomes: {biomeSummary.Trim()}. ";
                }
            }
            return report;
        }

        public void Update()
        {
            if (Time.time - _lastUpdate > UpdateInterval)
            {
                _lastUpdate = Time.time;
                RunCycle();
            }
        }

        private void RunCycle()
        {
            if (World.world == null) return;

            DetectAndHandleWorldChange();

            // MEMORY LEAK FIX: Remove dead/destroyed kingdoms so GC can free their memory
            var deadKingdoms = new System.Collections.Generic.List<Kingdom>();
            foreach (var k in _brains.Keys)
                if (k == null || !k.isAlive() || !k.isCiv()) deadKingdoms.Add(k);
            foreach (var dk in deadKingdoms) _brains.Remove(dk);

            // Refresh real-time database FIRST so all AI has accurate data this cycle
            RealTimeDB.Refresh();

            // Update World Tension
            UpdateWorldTension();

            foreach (Kingdom kingdom in World.world.kingdoms.list)
            {
                if (!kingdom.isAlive() || !kingdom.isCiv()) continue;

                if (!_brains.ContainsKey(kingdom)) _brains[kingdom] = new KingdomBrain();
                KingdomBrain brain = _brains[kingdom];

                if (brain.LoreHistory.Count == 0)
                {
                    string race = (kingdom.king != null && kingdom.king.asset != null) ? kingdom.king.asset.id : "civ";
                    brain.LoreHistory.Add($"The kingdom of {kingdom.name} was founded by the {race} people.");
                    brain.LoreHistory.Add($"Our culture is {kingdom.culture?.name ?? "unknown"}, our faith is {kingdom.religion?.name ?? "unknown"}.");
                }

                // 1. Record real economic memory BEFORE any action
                brain.MemoryBank.Record(kingdom);

                // 2. Detect state changes and incoming events since last cycle
                var newEvents = brain.EventTracker.DetectEvents(kingdom);
                if (newEvents.Count > 0)
                {
                    foreach (var evt in newEvents.Take(5))
                        brain.Memory.Add($"EVENT: {evt.Description}");
                }

                // 3. Perception & Empire Management
                UpdateEmpireData(kingdom, brain);
                string context = RealTimeDB.BuildContextString(kingdom, brain) + GetGeographicContext(kingdom);

                HandleSuccession(kingdom, brain);

                // 3. Update Physical Missions (spies, trade, settlement)
                UpdateMissions(kingdom, brain);

                // 3.5 Update Multi-Turn Plans
                UpdatePlanProgress(kingdom, brain);

                // 4. Stance Specific Behaviors
                if (brain.Stance == MilitaryStance.ScorchedEarth)
                {
                    var wars = World.world.wars.getWars(kingdom);
                    if (wars != null && wars.Any() && kingdom.countCities() > 0)
                    {
                        if (UnityEngine.Random.value < 0.1f) AdvancedKingdomActions.Sabotage(kingdom, kingdom);
                    }
                }

                // Survival Mode Logic (High Tension)
                if (GlobalState.WorldTension > 0.9f)
                {
                    brain.Focus = NationalFocus.Military;
                    brain.Stance = MilitaryStance.Blitzkrieg;
                    brain.MilitaryBudget = BudgetLevel.Aggressive;
                }

                // 5. Thought (Internal or External Provider)
                if (AIProviderClient.Instance != null && brain.Config.Provider != AIProvider.Internal)
                {
                    string history = string.Join(", ", brain.Memory);
                    string systemPrompt = AIProviderClient.Instance.GetSystemPrompt(kingdom, brain);
                    string prompt = $"{systemPrompt}\n{context}\nHistory: {history}";

                    AIProviderClient.Instance.Ask(kingdom, brain, prompt, (response) => {
                        try {
                            string cleanResponse = response.Replace("**", "");
                            string[] parts = cleanResponse.Split(new string[] { "ACTION:" }, StringSplitOptions.None);
                            brain.LastThink = parts[0].Replace("THOUGHT:", "").Trim();
                            string decision = parts.Length > 1 ? parts[1].Replace("[", "").Replace("]", "").Trim().ToUpper() : "STAY_STILL";

                            ExecuteAction(kingdom, decision);
                            brain.Memory.Add($"{Time.time}: {brain.Config.Provider} decided {decision}");
                        } catch (Exception e) {
                            brain.LastThink = "Error in thinking cycle.";
                            Debug.LogWarning($"[AIBox] AI response parse error: {e.Message}");
                        }
                    });
                }
                else
                {
                    string decision = brain.DecideAction(context, kingdom);
                    ExecuteAction(kingdom, decision);
                    brain.LastThink = $"Decision based on {brain.Personality} personality.";
                    brain.Memory.Add($"{Time.time}: Internal AI decided {decision}");
                }

                // Trim memory
                if (brain.Memory.Count > 10) brain.Memory.RemoveAt(0);

                // Mark all undelivered mail as delivered for this kingdom (they saw it in context)
                var newlyDeliveredMessages = MailRegistry.GetUnreadInbox(kingdom.name).Where(m => !m.IsDelivered).ToList();
                int newlyDelivered = MailRegistry.MarkDelivered(kingdom.name);
                if (newlyDelivered > 0)
                {
                    brain.Memory.Add($"MAIL: {newlyDelivered} new diplomatic message(s) received this cycle.");
                    foreach (var msg in newlyDeliveredMessages.Take(3))
                    {
                        brain.EventTracker.RecordIncomingMail(msg.SenderKingdom, msg.Subject, msg.Body);
                    }
                }
            }
        }

        private void UpdateWorldTension()
        {
            float targetTension = 0f;
            int warCount = World.world.wars.list.Count;
            targetTension += warCount * 0.15f;

            GlobalState.WorldTension = Mathf.MoveTowards(GlobalState.WorldTension, targetTension, 0.01f);

            WorldPhase prevPhase = GlobalState.CurrentPhase;
            if (GlobalState.WorldTension > 0.8f) GlobalState.CurrentPhase = WorldPhase.TotalWar;
            else if (GlobalState.WorldTension > 0.4f) GlobalState.CurrentPhase = WorldPhase.Tense;
            else GlobalState.CurrentPhase = WorldPhase.Stable;

            if (prevPhase != GlobalState.CurrentPhase)
            {
                GlobalState.GlobalNews.Add($"World entered {GlobalState.CurrentPhase} phase (tension: {GlobalState.WorldTension:P0})");
                if (GlobalState.GlobalNews.Count > 20) GlobalState.GlobalNews.RemoveAt(0);
            }
        }

        private void UpdateEmpireData(Kingdom k, KingdomBrain brain)
        {
            brain.CityData.Clear();
            brain.ControlledLuxuries.Clear();

            foreach (City city in k.getCities())
            {
                if (city == null || city.isRekt()) continue;

                string luxury = GlobalState.LuxuryTypes[Math.Abs(city.name.GetHashCode()) % GlobalState.LuxuryTypes.Length];

                var state = new CityEconomicState
                {
                    Name = city.name,
                    Gold = city.getResourcesAmount("gold"),
                    Food = city.getResourcesAmount("wheat"),
                    IsDistressed = city.status.hungry > 0 || city.status.homeless > 0 || city.getCachedLoyalty() < 0,
                    LuxuryResource = luxury
                };

                brain.CityData[city.name] = state;
                if (!brain.ControlledLuxuries.Contains(luxury)) brain.ControlledLuxuries.Add(luxury);
            }
        }

        private void HandleSuccession(Kingdom k, KingdomBrain brain)
        {
            if (k.king == null) return;

            try
            {
                var data = k.king.getData();
                if (data == null) return;

                string currentID = data.id.ToString();
                if (brain.CurrentKingID != currentID)
                {
                    brain.CurrentKingID = currentID;
                    data.favorite = true;
                    brain.Memory.Add($"SUCCESSION: New ruler {k.king.getName()} selected.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIBox] Succession check failed: {e.Message}");
            }
        }

        public Dictionary<Kingdom, KingdomBrain> GetBrains() => _brains;

        public KingdomBrain GetBrainForKingdom(Kingdom k)
        {
            if (k == null || _brains == null) return null;
            _brains.TryGetValue(k, out var brain);
            return brain;
        }

        private void ExecuteAction(Kingdom k, string decision)
        {
            if (!_brains.ContainsKey(k)) return;
            var brain = _brains[k];

            ActionRecord record = brain.BeginAction(decision, k);
            brain.LastAction = $"ACTION: {decision}";

            // Track this action in the event tracker so the AI knows what it ordered
            string actionType = decision.Contains(":") ? decision.Split(':')[0] : decision;
            string actionTarget = decision.Contains(":") ? decision.Substring(decision.IndexOf(':') + 1).Trim() : "";
            brain.EventTracker.RecordOurAction(actionType, actionTarget);

            // --- MAIL / DIPLOMACY ---
            if (decision.StartsWith("SEND_MAIL:"))
            {
                // Format: SEND_MAIL: KingdomName ~ Subject ~ Body
                // We use ~ as separator to avoid conflict with | used in THOUGHT/ACTION format
                string payload = decision.Substring("SEND_MAIL:".Length);
                string[] parts = payload.Split('~');
                if (parts.Length >= 3)
                {
                    string targetName = parts[0].Trim();
                    string subject = parts[1].Trim();
                    string body = parts[2].Trim();

                    Kingdom target = World.world.kingdoms.list.FirstOrDefault(x =>
                        x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

                    if (target != null)
                    {
                        // Optional opinion shift based on subject keywords
                        float opinionShift = 0f;
                        string lowerSub = subject.ToLowerInvariant();
                        if (lowerSub.Contains("insult") || lowerSub.Contains("threat") || lowerSub.Contains("war"))
                            opinionShift = -5f;
                        else if (lowerSub.Contains("gift") || lowerSub.Contains("praise") || lowerSub.Contains("ally"))
                            opinionShift = +5f;

                        var msg = MailRegistry.Send(k.name, target.name, subject, body, opinionShift);
                        brain.Memory.Add($"DIPLOMACY: Dispatched mail #{msg.Id} to {target.name}. Re: {subject}");

                        // If recipient has a brain, note it in their memory next cycle
                        if (_brains.ContainsKey(target))
                        {
                            _brains[target].Memory.Add($"MAIL: New message from {k.name} awaiting your attention. Subject: {subject}");
                        }
                    }
                    else
                    {
                        brain.Memory.Add($"DIPLOMACY: Failed to send mail — kingdom '{targetName}' not found.");
                    }
                }
                else if (parts.Length >= 2)
                {
                    // Fallback: no subject, treat parts[1] as body with generic subject
                    string targetName = parts[0].Trim();
                    string body = parts[1].Trim();
                    Kingdom target = World.world.kingdoms.list.FirstOrDefault(x =>
                        x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        var msg = MailRegistry.Send(k.name, target.name, "Diplomatic Note", body);
                        brain.Memory.Add($"DIPLOMACY: Dispatched mail #{msg.Id} to {target.name}.");
                        if (_brains.ContainsKey(target))
                            _brains[target].Memory.Add($"MAIL: New message from {k.name}. Subject: Diplomatic Note");
                    }
                }
                brain.EventTracker.ResolveAction("SEND_MAIL", actionTarget, true, "Mail dispatched.");
                brain.EndAction(record, k, "mail sent");
                return;
            }

            // --- WHISPER EXECUTION ---
            if (decision.StartsWith("EXECUTE_WHISPER_"))
            {
                string cmd = decision.Replace("EXECUTE_WHISPER_", "");
                if (cmd.Contains("WAR")) {
                     Kingdom target = FindBestWarTarget(k, brain);
                     if (target != null) {
                         World.world.wars.newWar(k, target, WarTypeLibrary.normal);
                         UI.NotificationManager.Instance?.Show("WAR DECLARED", $"<color=red>{k.name}</color> has declared war on <color=blue>{target.name}</color>!", Color.red);
                     }
                }
                if (cmd.Contains("PEACE")) {
                     var wars = World.world.wars.getWars(k);
                     if (wars != null) foreach(var war in wars) {
                         World.world.wars.endWar(war);
                         UI.NotificationManager.Instance?.Show("PEACE TREATY", $"<color=green>{k.name}</color> has ended the war: {war.name}", Color.green);
                     }
                }
                if (cmd.Contains("REBEL")) {
                     if (k.countCities() > 1) {
                         var cityList = k.getCities().ToList();
                         City c = cityList[UnityEngine.Random.Range(0, cityList.Count)];
                         if (c.leader != null) DiplomacyHelpersRebellion.startRebellion(c.leader, new Plot(), false);
                     }
                }
                brain.EndAction(record, k, "whisper executed");
                return;
            }

            // --- POLICY SETTERS ---
            if (decision.StartsWith("SET_STANCE:"))
            {
                string stanceStr = decision.Replace("SET_STANCE:", "").Trim();
                if (Enum.TryParse(stanceStr, true, out MilitaryStance s)) brain.Stance = s;
                brain.EventTracker.ResolveAction("SET_STANCE", stanceStr, true, $"Stance set to {s}.");
                brain.EndAction(record, k, $"stance set to {s}");
                return;
            }

            if (decision.StartsWith("SET_FOCUS:"))
            {
                string focusStr = decision.Replace("SET_FOCUS:", "").Trim();
                if (Enum.TryParse(focusStr, true, out NationalFocus f)) brain.Focus = f;
                brain.EventTracker.ResolveAction("SET_FOCUS", focusStr, true, $"Focus set to {f}.");
                brain.EndAction(record, k, $"focus set to {f}");
                return;
            }

            if (decision.StartsWith("SET_TAX:"))
            {
                string taxStr = decision.Replace("SET_TAX:", "").Trim();
                if (Enum.TryParse(taxStr, true, out TaxLevel t)) brain.TaxRate = t;
                brain.EventTracker.ResolveAction("SET_TAX", taxStr, true, $"Tax set to {t}.");
                brain.EndAction(record, k, $"tax set to {t}");
                return;
            }

            if (decision.StartsWith("SET_BUDGET:"))
            {
                string budgetStr = decision.Replace("SET_BUDGET:", "").Trim();
                if (Enum.TryParse(budgetStr, true, out BudgetLevel b)) brain.MilitaryBudget = b;
                brain.EventTracker.ResolveAction("SET_BUDGET", budgetStr, true, $"Budget set to {b}.");
                brain.EndAction(record, k, $"budget set to {b}");
                return;
            }

            // --- ESPIONAGE ---
            if (decision.StartsWith("RECRUIT_SPY"))
            {
                Kingdom target = FindSmartTarget(k, brain);
                if (target != null) {
                    AdvancedKingdomActions.RecruitSpy(k, target, brain, "Infiltrate and Sabotage");
                    brain.EventTracker.ResolveAction("RECRUIT_SPY", target.name, true, $"Spy recruited against {target.name}.");
                } else {
                    brain.EventTracker.ResolveAction("RECRUIT_SPY", "", false, "No valid target.");
                }
                brain.EndAction(record, k, "spy recruited");
                return;
            }

            if (decision.StartsWith("GATHER_INTEL:"))
            {
                string targetName = decision.Replace("GATHER_INTEL:", "").Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null) {
                    AdvancedKingdomActions.RecruitSpy(k, target, brain, "Gather Intelligence");
                    brain.EventTracker.ResolveAction("GATHER_INTEL", targetName, true, $"Intel mission started against {targetName}.");
                } else {
                    brain.EventTracker.ResolveAction("GATHER_INTEL", targetName, false, $"Target '{targetName}' not found.");
                }
                brain.EndAction(record, k, "intel mission started");
                return;
            }

            // --- TRADE (real game mechanic - trade caravans) ---
            if (decision.StartsWith("START_TRADE:"))
            {
                string targetName = decision.Replace("START_TRADE:", "").Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null) {
                    AdvancedKingdomActions.StartTradeCaravan(k, target, brain);
                    brain.EventTracker.ResolveAction("START_TRADE", targetName, true, $"Trade caravan dispatched to {targetName}.");
                } else {
                    brain.EventTracker.ResolveAction("START_TRADE", targetName, false, $"Target '{targetName}' not found.");
                }
                brain.EndAction(record, k, "trade caravan dispatched");
                return;
            }

            // --- SETTLEMENT ---
            if (decision.StartsWith("PLAN_CITY:"))
            {
                string siteName = decision.Replace("PLAN_CITY:", "").Trim();
                AdvancedKingdomActions.PlanSettlement(k, brain, siteName);
                brain.EventTracker.ResolveAction("PLAN_CITY", siteName, true, $"Settlement planned at {siteName}.");
                brain.EndAction(record, k, "settlement planned");
                return;
            }

            if (decision == "PLAN_CITY")
            {
                AdvancedKingdomActions.PlanSettlement(k, brain);
                brain.EventTracker.ResolveAction("PLAN_CITY", "", true, "Settlement planned.");
                brain.EndAction(record, k, "settlement planned");
                return;
            }

            // --- DIPLOMACY ---
            if (decision.StartsWith("FORM_ALLIANCE:"))
            {
                string targetName = decision.Replace("FORM_ALLIANCE:", "").Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null) {
                    AdvancedKingdomActions.FormAlliance(k, target, brain);
                    brain.EventTracker.ResolveAction("FORM_ALLIANCE", targetName, true, $"Alliance formed with {targetName}.");
                } else {
                    brain.EventTracker.ResolveAction("FORM_ALLIANCE", targetName, false, $"Target '{targetName}' not found.");
                }
                brain.EndAction(record, k, "alliance formed");
                return;
            }

            if (decision.StartsWith("OFFER_PEACE:"))
            {
                string targetName = decision.Replace("OFFER_PEACE:", "").Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null) {
                    AdvancedKingdomActions.OfferPeace(k, target, brain);
                    brain.EventTracker.ResolveAction("OFFER_PEACE", targetName, true, $"Peace offered to {targetName}.");
                } else {
                    brain.EventTracker.ResolveAction("OFFER_PEACE", targetName, false, $"Target '{targetName}' not found.");
                }
                brain.EndAction(record, k, "peace offered");
                return;
            }

            if (decision.StartsWith("BRIBE_CITY:"))
            {
                string targetCityName = decision.Replace("BRIBE_CITY:", "").Trim();
                AdvancedKingdomActions.BribeCity(k, targetCityName, brain);
                brain.EventTracker.ResolveAction("BRIBE_CITY", targetCityName, true, $"Bribe attempted on {targetCityName}.");
                brain.EndAction(record, k, "bribe attempted");
                return;
            }

            if (decision.StartsWith("ASSASSINATE_CHIEF:"))
            {
                string targetName = decision.Replace("ASSASSINATE_CHIEF:", "").Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null) {
                    AdvancedKingdomActions.AssassinateChief(k, target, brain);
                    brain.EventTracker.ResolveAction("ASSASSINATE_CHIEF", targetName, true, $"Assassination attempted on chief of {targetName}.");
                } else {
                    brain.EventTracker.ResolveAction("ASSASSINATE_CHIEF", targetName, false, $"Target '{targetName}' not found.");
                }
                brain.EndAction(record, k, "assassination attempted");
                return;
            }

            // --- PLOT / INTRIGUE SYSTEM ---
            if (decision.StartsWith("START_PLOT:"))
            {
                string payload = decision.Substring(11).Trim(); // "START_PLOT:".Length = 11
                string[] parts = payload.Split('~');
                string plotType = parts[0].Trim().ToLowerInvariant();
                string targetName = parts.Length > 1 ? parts[1].Trim() : "";

                PlotAsset plotAsset = AssetManager.plots_library.get(plotType);
                if (plotAsset == null)
                {
                    brain.EventTracker.ResolveAction("START_PLOT", plotType, false, $"Unknown plot type '{plotType}'.");
                    brain.EndAction(record, k, $"unknown plot type: {plotType}");
                    return;
                }

                if (k.king == null || !k.king.isAlive())
                {
                    brain.EventTracker.ResolveAction("START_PLOT", plotType, false, "No king available to start plot.");
                    brain.EndAction(record, k, "no king for plot");
                    return;
                }

                // Check requirements (but allow forced start)
                bool canDo = plotAsset.checkIsPossible(k.king);
                if (!canDo)
                {
                    // Still try forced — the game may allow it
                    Debug.LogWarning($"[AIBox] Plot {plotType} requirements not met for {k.name} king, but trying forced start.");
                }

                // Set target if needed
                if (!string.IsNullOrEmpty(targetName))
                {
                    Kingdom targetK = World.world.kingdoms.list.FirstOrDefault(x =>
                        x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    if (targetK != null)
                    {
                        // For some plots, we need to set target_kingdom manually if try_to_start_advanced doesn't
                        // The native tryStartPlot handles this for new_war, etc.
                    }
                }

                try
                {
                    bool started = World.world.plots.tryStartPlot(k.king, plotAsset, true);
                    if (started)
                    {
                        string desc = string.IsNullOrEmpty(targetName) ? plotType : $"{plotType} ~ {targetName}";
                        brain.EventTracker.RecordPlotStarted(plotType, targetName, true);
                        brain.EventTracker.ResolveAction("START_PLOT", desc, true, $"Plot '{plotType}' started.");
                        brain.Memory.Add($"INTRIGUE: Started '{plotType}' plot.");
                        brain.EndAction(record, k, $"plot {plotType} started");
                    }
                    else
                    {
                        brain.EventTracker.ResolveAction("START_PLOT", plotType, false, "Plot could not be started (requirements not met or already running).");
                        brain.EndAction(record, k, $"plot {plotType} failed to start");
                    }
                }
                catch (Exception ex)
                {
                    brain.EventTracker.ResolveAction("START_PLOT", plotType, false, $"Exception: {ex.Message}");
                    brain.EndAction(record, k, $"plot error: {ex.Message}");
                }
                return;
            }

            // --- WORLD LAWS ---
            if (decision.StartsWith("ADVOCATE_LAW:"))
            {
                string payload = decision.Substring(13).Trim(); // "ADVOCATE_LAW:".Length = 13
                string[] parts = payload.Split('~');
                string lawId = parts[0].Trim().ToLowerInvariant();
                string stateStr = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "toggle";
                bool enable = stateStr == "enable" || stateStr == "on" || stateStr == "true";
                bool disable = stateStr == "disable" || stateStr == "off" || stateStr == "false";

                // Normalize law ID
                if (!lawId.StartsWith("world_law_"))
                    lawId = "world_law_" + lawId.Replace(" ", "_");

                if (!WorldLawTracker.IsValidLaw(lawId))
                {
                    brain.EventTracker.ResolveAction("ADVOCATE_LAW", lawId, false, $"Unknown law '{lawId}'.");
                    brain.EndAction(record, k, $"unknown law: {lawId}");
                    return;
                }

                // Get current state
                var law = AssetManager.world_laws_library.get(lawId);
                bool currentState = law.isEnabled();
                bool targetState = enable ? true : (disable ? false : !currentState);

                if (currentState == targetState)
                {
                    brain.EventTracker.ResolveAction("ADVOCATE_LAW", lawId, false, $"Law is already {(targetState ? "enabled" : "disabled")}.");
                    brain.EndAction(record, k, $"law already {(targetState ? "enabled" : "disabled")}");
                    return;
                }

                // Toggle the law
                bool success = WorldLawTracker.ToggleLaw(lawId, targetState);
                if (success)
                {
                    string shortName = lawId.Replace("world_law_", "").Replace("_", " ");
                    string actionDesc = $"{(targetState ? "enabled" : "disabled")} world law: {shortName}";
                    brain.EventTracker.RecordGameEvent("WORLD_LAW", actionDesc);
                    brain.EventTracker.ResolveAction("ADVOCATE_LAW", lawId, true, actionDesc);
                    brain.Memory.Add($"POLICY: {actionDesc}. This affects ALL kingdoms.");
                    GlobalState.GlobalNews.Add($"{k.name} advocated for world law change: {shortName} is now {(targetState ? "ON" : "OFF")}!");
                    if (GlobalState.GlobalNews.Count > 20) GlobalState.GlobalNews.RemoveAt(0);
                    brain.EndAction(record, k, actionDesc);
                }
                else
                {
                    brain.EventTracker.ResolveAction("ADVOCATE_LAW", lawId, false, "Failed to toggle law.");
                    brain.EndAction(record, k, "law toggle failed");
                }
                return;
            }

            // --- CITY-LEVEL MANAGEMENT ---
            if (decision.StartsWith("SET_CITY_PRIORITY:"))
            {
                string payload = decision.Substring(18).Trim(); // after "SET_CITY_PRIORITY:"
                string[] parts = payload.Split('~');
                string cityName = parts.Length > 0 ? parts[0].Trim() : "";
                string priorityStr = parts.Length > 1 ? parts[1].Trim() : "Balanced";
                City city = k.getCities().FirstOrDefault(c => c.name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
                if (city != null && Enum.TryParse(priorityStr, true, out CityPriority cp))
                {
                    brain.CityPriorities[cityName] = cp;
                    // Influence the city by forcing its check cycle
                    city.executeAllActionsForCity();
                    brain.EventTracker.ResolveAction("SET_CITY_PRIORITY", $"{cityName} -> {cp}", true, $"City {cityName} priority set to {cp}.");
                    brain.EndAction(record, k, $"city priority {cityName}={cp}");
                }
                else
                {
                    brain.EventTracker.ResolveAction("SET_CITY_PRIORITY", cityName, false, $"City '{cityName}' not found or invalid priority.");
                    brain.EndAction(record, k, "city priority failed");
                }
                return;
            }

            if (decision.StartsWith("CONSCRIPT:"))
            {
                string payload = decision.Substring(10).Trim();
                string[] parts = payload.Split('~');
                string cityName = parts.Length > 0 ? parts[0].Trim() : "";
                int count = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int c) ? c : 1;
                City city = k.getCities().FirstOrDefault(c => c.name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
                int conscripted = 0;
                if (city != null)
                {
                    var citizens = k.getUnits().Where(a => a != null && a.isAlive() && a.city == city && a.isProfession(UnitProfession.Unit)).ToList();
                    foreach (var citizen in citizens.Take(count))
                    {
                        if (city.checkCanMakeWarrior(citizen))
                        {
                            city.makeWarrior(citizen);
                            conscripted++;
                        }
                    }
                    brain.EventTracker.ResolveAction("CONSCRIPT", $"{cityName} x{conscripted}", conscripted > 0, $"Conscripted {conscripted} warriors in {cityName}.");
                    brain.EndAction(record, k, $"conscripted {conscripted} in {cityName}");
                }
                else
                {
                    brain.EventTracker.ResolveAction("CONSCRIPT", cityName, false, $"City '{cityName}' not found.");
                    brain.EndAction(record, k, "conscript failed");
                }
                return;
            }

            if (decision.StartsWith("FORCE_CITY_CHECK:"))
            {
                string cityName = decision.Substring(17).Trim();
                City city = k.getCities().FirstOrDefault(c => c.name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
                if (city != null)
                {
                    city.executeAllActionsForCity();
                    brain.EventTracker.ResolveAction("FORCE_CITY_CHECK", cityName, true, $"Forced city check for {cityName}.");
                    brain.EndAction(record, k, $"forced check {cityName}");
                }
                else
                {
                    brain.EventTracker.ResolveAction("FORCE_CITY_CHECK", cityName, false, $"City '{cityName}' not found.");
                    brain.EndAction(record, k, "force check failed");
                }
                return;
            }

            // --- ENHANCED ESPIONAGE ---
            if (decision.StartsWith("STEAL_GOLD:"))
            {
                string targetName = decision.Substring(10).Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null && target.capital != null)
                {
                    int stolen = UnityEngine.Random.Range(10, 50);
                    brain.EventTracker.ResolveAction("STEAL_GOLD", targetName, true, $"Spy stole {stolen} gold from {targetName}. (Spy remains active)");
                    brain.Memory.Add($"ESPIONAGE: Spy infiltrated {targetName} treasury and extracted {stolen}g worth of intelligence.");
                    brain.EndAction(record, k, $"gold theft from {targetName}");
                }
                else
                {
                    brain.EventTracker.ResolveAction("STEAL_GOLD", targetName, false, $"Target '{targetName}' not found.");
                    brain.EndAction(record, k, "steal gold failed");
                }
                return;
            }

            if (decision.StartsWith("SABOTAGE_BUILDINGS:"))
            {
                string targetName = decision.Substring(19).Trim();
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (target != null && target.capital != null)
                {
                    var buildings = target.capital.buildings?.ToList() ?? new List<Building>();
                    int destroyed = 0;
                    foreach (var b in buildings.Where(b => b != null && b.isAlive() && UnityEngine.Random.value < 0.3f).Take(3))
                    {
                        try { b.startDestroyBuilding(); destroyed++; } catch { }
                    }
                    brain.EventTracker.ResolveAction("SABOTAGE_BUILDINGS", targetName, destroyed > 0, $"Destroyed {destroyed} buildings in {targetName}.");
                    brain.Memory.Add($"ESPIONAGE: Spy sabotaged {destroyed} buildings in {targetName}.");
                    brain.EndAction(record, k, $"building sabotage in {targetName}");
                }
                else
                {
                    brain.EventTracker.ResolveAction("SABOTAGE_BUILDINGS", targetName, false, $"Target '{targetName}' not found.");
                    brain.EndAction(record, k, "building sabotage failed");
                }
                return;
            }

            // --- MULTI-TURN PLANNING ---
            if (decision.StartsWith("CREATE_PLAN:"))
            {
                string payload = decision.Substring(12).Trim();
                string[] parts = payload.Split('~');
                string planName = parts.Length > 0 ? parts[0].Trim().ToUpper() : "GENERIC";
                string targetKingdom = parts.Length > 1 ? parts[1].Trim() : "";
                string targetCity = parts.Length > 2 ? parts[2].Trim() : "";
                int duration = parts.Length > 3 && int.TryParse(parts[3].Trim(), out int d) ? d : 5;

                if (brain.CurrentPlan != null && brain.CurrentPlan.Status == PlanStatus.Active)
                {
                    brain.EventTracker.ResolveAction("CREATE_PLAN", planName, false, "Already have an active plan. Cancel it first.");
                    brain.EndAction(record, k, "plan creation failed - active plan exists");
                    return;
                }

                brain.CurrentPlan = new StrategicPlan
                {
                    PlanID = System.Guid.NewGuid().ToString("N"),
                    Name = planName,
                    TurnStarted = (int)Time.time,
                    TargetTurns = duration,
                    TurnsElapsed = 0,
                    Status = PlanStatus.Active,
                    TargetKingdom = targetKingdom,
                    TargetCity = targetCity,
                    Description = $"{planName} against {targetKingdom} (target: {targetCity})"
                };
                brain.EventTracker.ResolveAction("CREATE_PLAN", planName, true, $"Started {planName} plan targeting {targetKingdom}. Duration: {duration} turns.");
                brain.EndAction(record, k, $"plan created: {planName}");
                return;
            }

            if (decision == "CANCEL_PLAN")
            {
                if (brain.CurrentPlan != null && brain.CurrentPlan.Status == PlanStatus.Active)
                {
                    brain.CurrentPlan.Status = PlanStatus.Cancelled;
                    brain.CompletedPlans.Add($"Cancelled: {brain.CurrentPlan.Name} at turn {(int)Time.time}");
                    brain.EventTracker.ResolveAction("CANCEL_PLAN", brain.CurrentPlan.Name, true, $"Plan {brain.CurrentPlan.Name} cancelled.");
                    brain.EndAction(record, k, $"plan cancelled: {brain.CurrentPlan.Name}");
                    brain.CurrentPlan = null;
                }
                else
                {
                    brain.EventTracker.ResolveAction("CANCEL_PLAN", "", false, "No active plan to cancel.");
                    brain.EndAction(record, k, "cancel plan failed");
                }
                return;
            }

            // --- STRATEGIC ACTIONS ---
            switch (decision)
            {
                case "LEAVE_ALLIANCE":
                    AdvancedKingdomActions.LeaveAlliance(k, brain);
                    brain.EventTracker.ResolveAction("LEAVE_ALLIANCE", "", true, "Left alliance.");
                    brain.EndAction(record, k, "left alliance");
                    break;

                case "PRAY_FOR_MIRACLE":
                    AdvancedKingdomActions.PrayForMiracle(k, brain);
                    brain.EventTracker.ResolveAction("PRAY_FOR_MIRACLE", "", true, "Prayed for miracle.");
                    brain.EndAction(record, k, "prayed for miracle");
                    break;

                case "DECLARE_WAR":
                    Kingdom targetWar = FindBestWarTarget(k, brain);
                    if (targetWar != null) {
                        AdvancedKingdomActions.DeclareJustifiedWar(k, targetWar, "Expansion");
                        brain.EventTracker.ResolveAction("DECLARE_WAR", targetWar.name, true, $"War declared on {targetWar.name}.");
                        brain.EndAction(record, k, $"war declared on {targetWar.name}");
                    } else {
                        brain.EventTracker.ResolveAction("DECLARE_WAR", "", false, "No valid target for war.");
                        brain.EndAction(record, k, "no valid target for war");
                    }
                    break;

                case "ASSASSINATE":
                    Kingdom targetAssassinate = FindSmartTarget(k, brain);
                    if (targetAssassinate != null) {
                        AdvancedKingdomActions.Assassinate(k, targetAssassinate);
                        brain.EventTracker.ResolveAction("ASSASSINATE", targetAssassinate.name, true, $"Assassination attempted on {targetAssassinate.name}.");
                        brain.EndAction(record, k, $"assassination attempted on {targetAssassinate.name}");
                    } else {
                        brain.EventTracker.ResolveAction("ASSASSINATE", "", false, "No valid target.");
                        brain.EndAction(record, k, "no valid target");
                    }
                    break;

                case "SABOTAGE":
                    Kingdom targetSabotage = FindSmartTarget(k, brain);
                    if (targetSabotage != null) {
                        AdvancedKingdomActions.Sabotage(k, targetSabotage);
                        brain.EventTracker.ResolveAction("SABOTAGE", targetSabotage.name, true, $"Sabotage on {targetSabotage.name}.");
                        brain.EndAction(record, k, $"sabotage on {targetSabotage.name}");
                    } else {
                        brain.EventTracker.ResolveAction("SABOTAGE", "", false, "No valid target.");
                        brain.EndAction(record, k, "no valid target");
                    }
                    break;

                case "WAR":
                    Kingdom enemy = FindBestWarTarget(k, brain);
                    if (enemy != null) {
                        World.world.wars.newWar(k, enemy, WarTypeLibrary.normal);
                        UI.NotificationManager.Instance?.Show("WAR DECLARED", $"<color=red>{k.name}</color> has declared war on <color=blue>{enemy.name}</color>!", Color.red);
                        GlobalState.GlobalNews.Add($"{k.name} declared war on {enemy.name}!");
                        if (GlobalState.GlobalNews.Count > 20) GlobalState.GlobalNews.RemoveAt(0);
                        brain.EventTracker.ResolveAction("WAR", enemy.name, true, $"War on {enemy.name}.");
                        brain.EndAction(record, k, $"war on {enemy.name}");
                    } else {
                        brain.EventTracker.ResolveAction("WAR", "", false, "No enemy found.");
                        brain.EndAction(record, k, "no enemy found");
                    }
                    break;

                case "PEACE":
                    var activeWars = World.world.wars.getWars(k);
                    var warList = activeWars?.ToList();
                    if (warList != null && warList.Count > 0) {
                        foreach (var w in warList) {
                            World.world.wars.endWar(w);
                            UI.NotificationManager.Instance?.Show("PEACE TREATY", $"<color=green>{k.name}</color> has ended the war: {w.name}", Color.green);
                            brain.EventTracker.ResolveAction("PEACE", w.name, true, $"Peace treaty with {w.name}.");
                            brain.EndAction(record, k, $"peace: {w.name}");
                            break;
                        }
                    } else {
                        brain.EventTracker.ResolveAction("PEACE", "", false, "No wars to end.");
                        brain.EndAction(record, k, "no wars to end");
                    }
                    break;

                case "HIRE_MERCENARIES":
                    AdvancedKingdomActions.HireMercenaries(k, brain);
                    brain.EventTracker.ResolveAction("HIRE_MERCENARIES", "", true, "Mercenaries hired.");
                    brain.EndAction(record, k, "mercenaries hired");
                    break;

                case "FESTIVAL":
                    AdvancedKingdomActions.HoldFestival(k, brain);
                    brain.EventTracker.ResolveAction("FESTIVAL", "", true, "Festival held.");
                    brain.EndAction(record, k, "festival held");
                    break;

                case "SURVEY_LAND":
                    AdvancedKingdomActions.SurveyLand(k, brain);
                    brain.EventTracker.ResolveAction("SURVEY_LAND", "", true, "Land surveyed.");
                    brain.EndAction(record, k, "land surveyed");
                    break;

                case "STRENGTHEN_CULTURE":
                    if (k.hasCulture()) {
                        string[] possibleTraits = { "roads", "buildings", "diplomacy", "trade", "warfare" };
                        string picked = null;
                        foreach (string t in possibleTraits)
                        {
                            if (!k.culture.hasTrait(t)) { picked = t; break; }
                        }
                        if (picked != null)
                        {
                            k.culture.addTrait(picked);
                            brain.Memory.Add($"PROGRESS: Our people have discovered the secret of {picked}.");
                            brain.EventTracker.ResolveAction("STRENGTHEN_CULTURE", picked, true, $"Added culture trait: {picked}.");
                            brain.EndAction(record, k, $"culture trait {picked} added");
                        }
                        else
                        {
                            brain.Memory.Add("PROGRESS: Our culture is already advanced in all known arts.");
                            brain.EventTracker.ResolveAction("STRENGTHEN_CULTURE", "", false, "No new traits available.");
                            brain.EndAction(record, k, "no new traits available");
                        }
                    } else {
                        brain.EventTracker.ResolveAction("STRENGTHEN_CULTURE", "", false, "No culture.");
                        brain.EndAction(record, k, "no culture");
                    }
                    break;

                // --- EMERGENCY SIGNALS (for internal AI, no-op for now but could trigger UI warnings) ---
                case "FOCUS_FOOD":
                    brain.Memory.Add("CRISIS: Mass hunger detected. We need more food production. Citizens must build more farms.");
                    brain.EventTracker.ResolveAction("FOCUS_FOOD", "", true, "Food crisis flagged.");
                    brain.EndAction(record, k, "food crisis flagged");
                    break;

                case "FOCUS_HOUSING":
                    brain.Memory.Add("CRISIS: Homelessness detected. We need more houses. Citizens must expand housing.");
                    brain.EventTracker.ResolveAction("FOCUS_HOUSING", "", true, "Housing crisis flagged.");
                    brain.EndAction(record, k, "housing crisis flagged");
                    break;

                case "RECRUIT_ARMY":
                    AdvancedKingdomActions.HireMercenaries(k, brain);
                    brain.EventTracker.ResolveAction("RECRUIT_ARMY", "", true, "Army recruitment.");
                    brain.EndAction(record, k, "army recruitment");
                    break;

                case "SABOTAGE_ENEMY":
                    Kingdom sabTarget = FindSmartTarget(k, brain);
                    if (sabTarget != null) {
                        AdvancedKingdomActions.Sabotage(k, sabTarget);
                        brain.EventTracker.ResolveAction("SABOTAGE_ENEMY", sabTarget.name, true, $"Sabotage on {sabTarget.name}.");
                    } else {
                        brain.EventTracker.ResolveAction("SABOTAGE_ENEMY", "", false, "No valid target.");
                    }
                    brain.EndAction(record, k, "sabotage attempted");
                    break;

                // --- COMBAT TACTICS ---
                case "SET_WAR_STANCE:AGGRESSIVE":
                    brain.WarStance = CombatStance.Aggressive;
                    brain.EventTracker.ResolveAction("SET_WAR_STANCE", "Aggressive", true, "War stance set to Aggressive.");
                    brain.EndAction(record, k, "war stance aggressive");
                    break;

                case "SET_WAR_STANCE:BALANCED":
                    brain.WarStance = CombatStance.Balanced;
                    brain.EventTracker.ResolveAction("SET_WAR_STANCE", "Balanced", true, "War stance set to Balanced.");
                    brain.EndAction(record, k, "war stance balanced");
                    break;

                case "SET_WAR_STANCE:DEFENSIVE":
                    brain.WarStance = CombatStance.Defensive;
                    brain.EventTracker.ResolveAction("SET_WAR_STANCE", "Defensive", true, "War stance set to Defensive.");
                    brain.EndAction(record, k, "war stance defensive");
                    break;

                case "SIEGE_CITY":
                    {
                        // Find enemy city to siege
                        var ourWars = World.world.wars.getWars(k)?.ToList() ?? new List<War>();
                        City siegeTarget = null;
                        foreach (var war in ourWars)
                        {
                            var foes = war.isAttacker(k) ? war.getDefenders() : war.getAttackers();
                            foreach (var foe in foes)
                            {
                                if (foe == null || !foe.isAlive()) continue;
                                siegeTarget = foe.getCities().FirstOrDefault(c => c != null && !c.isRekt());
                                if (siegeTarget != null) break;
                            }
                            if (siegeTarget != null) break;
                        }
                        if (siegeTarget != null)
                        {
                            brain.SiegeTargetCity = siegeTarget.name;
                            // Move our warriors toward the target city
                            foreach (var w in k.getUnits().Where(a => a != null && a.isAlive() && a.isProfession(UnitProfession.Warrior)))
                            {
                                w.goTo(siegeTarget.getTile());
                            }
                            brain.EventTracker.ResolveAction("SIEGE_CITY", siegeTarget.name, true, $"Laid siege to {siegeTarget.name}.");
                            brain.EndAction(record, k, $"siege on {siegeTarget.name}");
                        }
                        else
                        {
                            brain.EventTracker.ResolveAction("SIEGE_CITY", "", false, "No enemy city to siege.");
                            brain.EndAction(record, k, "siege failed");
                        }
                    }
                    break;

                case "SALLY":
                    {
                        // Attack from defensive position - all warriors charge nearest enemy city
                        City ourCapital = k.capital;
                        if (ourCapital != null)
                        {
                            var warriors = k.getUnits().Where(a => a != null && a.isAlive() && a.isProfession(UnitProfession.Warrior)).ToList();
                            City nearestEnemy = null;
                            float bestDist = float.MaxValue;
                            foreach (var other in World.world.kingdoms.list)
                            {
                                if (other == k || !other.isAlive()) continue;
                                foreach (var ec in other.getCities())
                                {
                                    if (ec == null || ec.isRekt()) continue;
                                    float d = Vector2.Distance(
                                        new Vector2(ourCapital.getTile().x, ourCapital.getTile().y),
                                        new Vector2(ec.getTile().x, ec.getTile().y));
                                    if (d < bestDist) { bestDist = d; nearestEnemy = ec; }
                                }
                            }
                            if (nearestEnemy != null && warriors.Count > 0)
                            {
                                foreach (var w in warriors) w.goTo(nearestEnemy.getTile());
                                brain.EventTracker.ResolveAction("SALLY", nearestEnemy.name, true, $"Sallied against {nearestEnemy.name} with {warriors.Count} warriors.");
                                brain.EndAction(record, k, $"sally against {nearestEnemy.name}");
                            }
                            else
                            {
                                brain.EventTracker.ResolveAction("SALLY", "", false, "No enemy city in range or no warriors.");
                                brain.EndAction(record, k, "sally failed");
                            }
                        }
                        else
                        {
                            brain.EventTracker.ResolveAction("SALLY", "", false, "No capital.");
                            brain.EndAction(record, k, "sally failed");
                        }
                    }
                    break;

                case "RETREAT":
                    {
                        // Pull all warriors back to capital
                        City capital = k.capital;
                        if (capital != null)
                        {
                            var warriors = k.getUnits().Where(a => a != null && a.isAlive() && a.isProfession(UnitProfession.Warrior)).ToList();
                            foreach (var w in warriors) w.goTo(capital.getTile());
                            brain.WarStance = CombatStance.Retreat;
                            brain.EventTracker.ResolveAction("RETREAT", capital.name, true, $"Retreated {warriors.Count} warriors to {capital.name}.");
                            brain.EndAction(record, k, $"retreat to {capital.name}");
                        }
                        else
                        {
                            brain.EventTracker.ResolveAction("RETREAT", "", false, "No capital to retreat to.");
                            brain.EndAction(record, k, "retreat failed");
                        }
                    }
                    break;

                case "STAY_STILL":
                default:
                    brain.EventTracker.ResolveAction("STAY_STILL", "", true, "Waiting.");
                    brain.EndAction(record, k, "waiting");
                    break;
            }
        }

        private void UpdateMissions(Kingdom k, KingdomBrain brain)
        {
            if (brain.ActiveMissions.Count == 0) return;

            List<ActiveMission> toRemove = new List<ActiveMission>();
            foreach (var mission in brain.ActiveMissions)
            {
                Actor actor = World.world.units.getSimpleList().FirstOrDefault(u => u.getData().id == long.Parse(mission.ActorID));
                if (actor == null || !actor.isAlive())
                {
                    toRemove.Add(mission);
                    continue;
                }

                switch (mission.Type)
                {
                    case MissionType.Espionage:
                        HandleEspionage(k, brain, mission, actor);
                        break;
                    case MissionType.Trade:
                        HandleTrade(k, brain, mission, actor);
                        break;
                    case MissionType.Settlement:
                        HandleSettlement(k, brain, mission, actor);
                        break;
                }

                if (mission.Progress >= 100f) toRemove.Add(mission);
            }

            foreach (var m in toRemove) brain.ActiveMissions.Remove(m);
        }

        private void HandleEspionage(Kingdom k, KingdomBrain brain, ActiveMission mission, Actor actor)
        {
            Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name == mission.TargetKingdom);
            if (target == null || target.capital == null) { mission.Progress = 100f; return; }

            if (!actor.isAlive())
            {
                brain.Memory.Add($"ESPIONAGE: Our spy died en route to {mission.TargetKingdom}.");
                brain.SpiesActive = Mathf.Max(0, brain.SpiesActive - 1);
                mission.Progress = 100f;
                return;
            }

            WorldTile targetTile = target.capital.getTile();
            if (targetTile == null) { mission.Progress = 100f; return; }
            actor.goTo(targetTile);

            if (actor.current_tile == null || targetTile == null) return;

            float dist = Vector2.Distance(new Vector2(actor.current_tile.x, actor.current_tile.y),
                                           new Vector2(targetTile.x, targetTile.y));
            if (dist > 5f) return;

            float roll = UnityEngine.Random.value;
            float catchChance = 0.25f;
            float dieChance = 0.10f;

            if (target.countTotalWarriors() > 5) catchChance += 0.15f;
            if (target.capital != null && target.capital.hasBuildingType("type_watch_tower", true, null)) catchChance += 0.10f;

            if (roll < dieChance)
            {
                actor.dieAndDestroy(AttackType.Other);
                brain.Memory.Add($"ESPIONAGE: Our spy died on the way to {target.name}. The journey was too dangerous.");
                brain.SpiesActive = Mathf.Max(0, brain.SpiesActive - 1);
                mission.Progress = 100f;
                return;
            }

            if (roll < dieChance + catchChance)
            {
                actor.dieAndDestroy(AttackType.Other);
                brain.Memory.Add($"ESPIONAGE: Our spy was CAUGHT and executed by {target.name}!");

                if (_brains.ContainsKey(target))
                {
                    _brains[target].Memory.Add($"SECURITY: Caught and executed a spy from {k.name}!");
                    MailRegistry.Send(k.name, target.name, "Spy Captured",
                        $"We captured and executed a spy from {k.name}. They are hostile toward us.", -10f);
                }

                brain.SpiesActive = Mathf.Max(0, brain.SpiesActive - 1);
                mission.Progress = 100f;
                return;
            }

            if (mission.Detail == "Gather Intelligence")
            {
                string intelReport = GatherIntelReport(k, target);
                brain.Memory.Add($"ESPIONAGE: Spy successfully infiltrated {target.name}. INTEL REPORT:\n{intelReport}");

                brain.IntelLevel[target.name] = brain.IntelLevel.ContainsKey(target.name)
                    ? brain.IntelLevel[target.name] + 2
                    : 2;

                brain.SpiesActive = Mathf.Max(0, brain.SpiesActive - 1);
                mission.Progress = 100f;
                actor.dieAndDestroy(AttackType.Other);
                return;
            }

            AdvancedKingdomActions.Sabotage(k, target);
            brain.Memory.Add($"ESPIONAGE: Our spy successfully sabotaged {target.name}.");
            brain.SpiesActive = Mathf.Max(0, brain.SpiesActive - 1);
            mission.Progress = 100f;
            actor.dieAndDestroy(AttackType.Other);
        }

        private string GatherIntelReport(Kingdom spyOwner, Kingdom target)
        {
            string report = "";
            report += $"- {target.name} has {target.countCities()} cities, {target.getPopulationPeople()} population, {target.countTotalWarriors()} warriors\n";

            if (RealTimeDB.Kingdoms.ContainsKey(target.name))
            {
                var snap = RealTimeDB.Kingdoms[target.name];
                report += $"- Total gold: {snap.TotalGold}, food: {snap.TotalFood}, wood: {snap.TotalWood}\n";
                report += $"- Active wars: {(snap.ActiveWars.Count > 0 ? string.Join(", ", snap.ActiveWars) : "None")}\n";
                report += $"- Allies: {(snap.AlliedKingdoms.Count > 0 ? string.Join(", ", snap.AlliedKingdoms) : "None")}\n";
                report += $"- Hungry: {snap.TotalHungry}, Homeless: {snap.TotalHomeless}, Avg Happiness: {snap.AvgHappiness:F0}\n";
            }

            if (target.king != null) report += $"- Ruler: {target.king.getName()}\n";
            if (target.capital != null) report += $"- Capital: {target.capital.name}\n";

            return report;
        }

        private void HandleTrade(Kingdom k, KingdomBrain brain, ActiveMission mission, Actor actor)
        {
            Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name == mission.TargetKingdom);
            if (target == null || target.capital == null) { mission.Progress = 100f; return; }

            WorldTile targetTile = target.capital.getTile();
            actor.goTo(targetTile);

            if (actor.current_tile == targetTile)
            {
                float bonus = 200;
                var tBrain = _brains.ContainsKey(target) ? _brains[target] : null;

                if (tBrain != null)
                {
                    int uniqueLuxuries = brain.ControlledLuxuries.Union(tBrain.ControlledLuxuries).Count();
                    bonus += (uniqueLuxuries * 50);
                }

                // NOTE: We no longer inject gold directly. The trade caravan arriving may trigger game mechanics.
                // We log it as a trade success but don't cheat resources.
                brain.Memory.Add($"TRADE: Trade mission to {target.name} completed. Diplomatic relations improved.");
                mission.Progress = 100f;
                actor.dieAndDestroy(AttackType.Other);
            }
        }

        private void HandleSettlement(Kingdom k, KingdomBrain brain, ActiveMission mission, Actor actor)
        {
            int tileIndex = (int)mission.Progress;
            int width = MapBox.width;
            int x = tileIndex % width;
            int y = tileIndex / width;

            WorldTile targetTile = World.world.GetTile(x, y);
            if (targetTile == null) { mission.Progress = 100f; return; }

            actor.goTo(targetTile);

            if (actor.current_tile == targetTile)
            {
                if (targetTile.zone != null && !targetTile.zone.hasCity() && actor.canBuildNewCity())
                {
                    try
                    {
                        World.world.cities.buildNewCity(actor, targetTile.zone);
                        brain.Memory.Add($"SUCCESS: Established a new colony at {targetTile.x}, {targetTile.y}.");
                    }
                    catch (Exception e)
                    {
                        brain.Memory.Add($"SETTLEMENT FAILED: {e.Message}");
                    }
                }
                else
                {
                    brain.Memory.Add("SETTLEMENT FAILED: Target zone already has a city or is unsuitable.");
                }
                mission.Progress = 100f;
                actor.dieAndDestroy(AttackType.Other);
            }
        }

        /// <summary>
        /// Smart targeting: picks the best war target based on opinion, military strength,
        /// proximity, alliance status, and current wars.
        /// </summary>
        private Kingdom FindBestWarTarget(Kingdom k, KingdomBrain brain)
        {
            var candidates = new List<(Kingdom target, float score)>();
            int ourArmy = k.countTotalWarriors();
            var ourWars = World.world.wars.getWars(k)?.ToList() ?? new List<War>();
            bool alreadyAtWar = ourWars.Count > 0;

            foreach (var other in World.world.kingdoms.list)
            {
                if (other == k || !other.isAlive() || !other.isCiv()) continue;

                // Skip allies
                if (k.hasAlliance() && other.hasAlliance() && k.getAlliance() == other.getAlliance())
                    continue;

                float score = 0f;

                // 1. Opinion: heavily favor hostile kingdoms
                try
                {
                    var opinion = World.world.diplomacy.getOpinion(k, other);
                    int opinionVal = opinion?.total ?? 0;
                    if (opinionVal < -30) score += 100f;        // Hostile: very good target
                    else if (opinionVal < 0) score += 50f;       // Unfriendly: decent target
                    else if (opinionVal > 30) score -= 200f;      // Friendly: avoid attacking
                    else if (opinionVal > 0) score -= 50f;        // Neutral: slightly avoid
                }
                catch { }

                // 2. Military strength: avoid attacking much stronger enemies
                int theirArmy = other.countTotalWarriors();
                if (theirArmy == 0) score += 30f; // Easy pickings
                else
                {
                    float ratio = (float)ourArmy / theirArmy;
                    if (ratio > 2.0f) score += 60f;      // We are much stronger
                    else if (ratio > 1.2f) score += 30f; // We are somewhat stronger
                    else if (ratio > 0.8f) score += 10f; // Roughly equal
                    else if (ratio > 0.5f) score -= 30f; // They are stronger
                    else score -= 100f;                   // They are much stronger
                }

                // 3. Proximity: prefer nearby kingdoms (simpler = has nearby cities)
                bool nearby = false;
                try
                {
                    foreach (var ourCity in k.getCities())
                    {
                        foreach (var theirCity in other.getCities())
                        {
                            if (ourCity == null || theirCity == null) continue;
                            float dist = Vector2.Distance(
                                new Vector2(ourCity.getTile().x, ourCity.getTile().y),
                                new Vector2(theirCity.getTile().x, theirCity.getTile().y));
                            if (dist < 30f) { nearby = true; break; }
                        }
                        if (nearby) break;
                    }
                }
                catch { }
                if (nearby) score += 40f;
                else score -= 20f; // Distant wars are hard to supply

                // 4. Multi-front penalty: avoid starting a war if already at war
                if (alreadyAtWar) score -= 50f;

                // 5. City count: prefer kingdoms with cities to conquer
                int theirCities = other.countCities();
                if (theirCities > 0) score += theirCities * 5f;

                // 6. Personality bonus
                if (brain.Personality == AIPersonality.Expansionist) score += 20f;
                if (brain.Personality == AIPersonality.Pacifist) score -= 50f;

                // 7. Already at war with them? Skip, we're already fighting
                bool alreadyAtWarWithThem = false;
                try
                {
                    foreach (var war in ourWars)
                    {
                        if (war.isAttacker(other) || war.isDefender(other)) { alreadyAtWarWithThem = true; break; }
                    }
                }
                catch { }
                if (alreadyAtWarWithThem) score -= 500f; // Don't declare war twice

                candidates.Add((other, score));
            }

            if (candidates.Count == 0) return null;

            // Sort by score descending
            candidates.Sort((a, b) => b.score.CompareTo(a.score));

            // Pick the top candidate if score is positive, otherwise no good target
            if (candidates[0].score > 0)
                return candidates[0].target;

            // If no good targets but we have candidates, pick the least bad
            return candidates[0].target;
        }

        /// <summary>
        /// Find a smart target for espionage/sabotage/assassination.
        /// Prefers hostile kingdoms that are not already at war with us.
        /// </summary>
        private Kingdom FindSmartTarget(Kingdom k, KingdomBrain brain)
        {
            var candidates = new List<(Kingdom target, float score)>();

            foreach (var other in World.world.kingdoms.list)
            {
                if (other == k || !other.isAlive() || !other.isCiv()) continue;

                // Skip allies
                if (k.hasAlliance() && other.hasAlliance() && k.getAlliance() == other.getAlliance())
                    continue;

                float score = 0f;

                // Hostile opinion = good target for covert ops
                try
                {
                    var opinion = World.world.diplomacy.getOpinion(k, other);
                    int opinionVal = opinion?.total ?? 0;
                    if (opinionVal < -30) score += 80f;
                    else if (opinionVal < 0) score += 40f;
                    else if (opinionVal > 30) score -= 100f;
                }
                catch { }

                // Prefer stronger enemies (more valuable to sabotage)
                int theirArmy = other.countTotalWarriors();
                score += theirArmy * 0.5f;

                // Nearby = easier to operate
                bool nearby = false;
                try
                {
                    foreach (var ourCity in k.getCities())
                    {
                        foreach (var theirCity in other.getCities())
                        {
                            if (ourCity == null || theirCity == null) continue;
                            float dist = Vector2.Distance(
                                new Vector2(ourCity.getTile().x, ourCity.getTile().y),
                                new Vector2(theirCity.getTile().x, theirCity.getTile().y));
                            if (dist < 30f) { nearby = true; break; }
                        }
                        if (nearby) break;
                    }
                }
                catch { }
                if (nearby) score += 20f;

                candidates.Add((other, score));
            }

            if (candidates.Count == 0) return null;
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            return candidates[0].target;
        }

        /// <summary>
        /// Legacy random enemy selector (kept for fallback).
        /// </summary>
        private Kingdom GetRandomEnemy(Kingdom k)
        {
            var validEnemies = new List<Kingdom>();
            foreach (var other in World.world.kingdoms.list)
            {
                if (other != k && other.isAlive() && other.isCiv()) validEnemies.Add(other);
            }
            if (validEnemies.Count == 0) return null;
            return validEnemies[UnityEngine.Random.Range(0, validEnemies.Count)];
        }

        /// <summary>
        /// Advance multi-turn plan progress each AI cycle.
        /// Marks plans as completed/failed and logs milestones.
        /// </summary>
        private void UpdatePlanProgress(Kingdom k, KingdomBrain brain)
        {
            if (brain.CurrentPlan == null || brain.CurrentPlan.Status != PlanStatus.Active)
                return;

            brain.CurrentPlan.TurnsElapsed++;
            string nextStep = brain.CurrentPlan.NextStep;

            // Auto-complete certain plans based on game state
            if (brain.CurrentPlan.Name.Contains("CONQUER"))
            {
                Kingdom target = World.world.kingdoms.list.FirstOrDefault(x => x.name.Equals(brain.CurrentPlan.TargetKingdom, StringComparison.OrdinalIgnoreCase));
                if (target == null || !target.isAlive())
                {
                    brain.CurrentPlan.Status = PlanStatus.Completed;
                    brain.CurrentPlan.StepsCompleted.Add($"Target {brain.CurrentPlan.TargetKingdom} eliminated at turn {(int)Time.time}");
                    brain.CompletedPlans.Add($"Completed: {brain.CurrentPlan.Name} - target destroyed");
                    brain.EventTracker.RecordGameEvent("PLAN_COMPLETE", $"Plan {brain.CurrentPlan.Name} completed: {brain.CurrentPlan.TargetKingdom} destroyed.");
                    brain.CurrentPlan = null;
                    return;
                }
                // Check if we captured the target city
                if (!string.IsNullOrEmpty(brain.CurrentPlan.TargetCity))
                {
                    var ourCities = k.getCities().Select(c => c.name).ToHashSet();
                    if (ourCities.Contains(brain.CurrentPlan.TargetCity))
                    {
                        brain.CurrentPlan.Status = PlanStatus.Completed;
                        brain.CurrentPlan.StepsCompleted.Add($"Captured {brain.CurrentPlan.TargetCity} at turn {(int)Time.time}");
                        brain.CompletedPlans.Add($"Completed: {brain.CurrentPlan.Name} - city captured");
                        brain.EventTracker.RecordGameEvent("PLAN_COMPLETE", $"Plan {brain.CurrentPlan.Name} completed: {brain.CurrentPlan.TargetCity} captured.");
                        brain.CurrentPlan = null;
                        return;
                    }
                }
            }
            else if (brain.CurrentPlan.Name.Contains("DEFEND"))
            {
                var ourWars = World.world.wars.getWars(k)?.ToList() ?? new List<War>();
                if (ourWars.Count == 0)
                {
                    brain.CurrentPlan.Status = PlanStatus.Completed;
                    brain.CurrentPlan.StepsCompleted.Add($"Threat eliminated at turn {(int)Time.time}");
                    brain.CompletedPlans.Add($"Completed: {brain.CurrentPlan.Name} - peace restored");
                    brain.EventTracker.RecordGameEvent("PLAN_COMPLETE", $"Plan {brain.CurrentPlan.Name} completed: peace restored.");
                    brain.CurrentPlan = null;
                    return;
                }
            }

            // Time limit check
            if (brain.CurrentPlan.TurnsElapsed >= brain.CurrentPlan.TargetTurns)
            {
                brain.CurrentPlan.Status = PlanStatus.Failed;
                brain.CompletedPlans.Add($"Expired: {brain.CurrentPlan.Name} after {brain.CurrentPlan.TargetTurns} turns");
                brain.EventTracker.RecordGameEvent("PLAN_EXPIRED", $"Plan {brain.CurrentPlan.Name} expired after {brain.CurrentPlan.TargetTurns} turns.");
                brain.CurrentPlan = null;
                return;
            }

            // Log plan progress every other turn
            if (brain.CurrentPlan.TurnsElapsed % 2 == 0)
            {
                brain.EventTracker.RecordGameEvent("PLAN_PROGRESS", $"Plan {brain.CurrentPlan.Name}: turn {brain.CurrentPlan.TurnsElapsed}/{brain.CurrentPlan.TargetTurns}, next: {nextStep}");
            }
        }

        // ==================== SAVE / LOAD PERSISTENCE ====================

        private const string SaveKeyPrefix = "AIBoxBrain_";

        /// <summary>
        /// Serialize all AI brain state to the game's save system.
        /// Call this before the game saves.
        /// </summary>
        public void SaveAllBrains()
        {
            int count = 0;
            foreach (var pair in _brains)
            {
                if (pair.Key == null || !pair.Key.isAlive()) continue;
                string json = SerializeBrain(pair.Value);
                PlayerPrefs.SetString($"{SaveKeyPrefix}{pair.Key.name}", json);
                count++;
            }
            PlayerPrefs.SetInt($"{SaveKeyPrefix}Count", count);
            PlayerPrefs.Save();
            Debug.Log($"[AIBox] Saved {count} brain states.");
        }

        /// <summary>
        /// Deserialize brain state after a game load.
        /// Call this after the world is fully loaded.
        /// </summary>
        public void LoadAllBrains()
        {
            if (!PlayerPrefs.HasKey($"{SaveKeyPrefix}Count")) return;
            int count = PlayerPrefs.GetInt($"{SaveKeyPrefix}Count", 0);
            Debug.Log($"[AIBox] Loading {count} brain states...");

            foreach (var k in World.world.kingdoms.list)
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                string json = PlayerPrefs.GetString($"{SaveKeyPrefix}{k.name}", "");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var brain = DeserializeBrain(json);
                        if (brain != null) _brains[k] = brain;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AIBox] Failed to load brain for {k.name}: {e.Message}");
                    }
                }
            }
            Debug.Log($"[AIBox] Loaded {_brains.Count} brain states.");
        }

        private string SerializeBrain(KingdomBrain brain)
        {
            var data = new BrainSaveData
            {
                BrainID = brain.BrainID,
                Personality = (int)brain.Personality,
                Ambition = brain.Ambition,
                Memory = brain.Memory,
                LoreHistory = brain.LoreHistory,
                TaxRate = (int)brain.TaxRate,
                MilitaryBudget = (int)brain.MilitaryBudget,
                Focus = (int)brain.Focus,
                Stance = (int)brain.Stance,
                WarStance = (int)brain.WarStance,
                CurrentKingID = brain.CurrentKingID,
                SpiesActive = brain.SpiesActive,
                IntelLevel = brain.IntelLevel,
                CityPriorities = brain.CityPriorities.ToDictionary(x => x.Key, x => (int)x.Value),
                CompletedPlans = brain.CompletedPlans,
                SiegeTargetCity = brain.SiegeTargetCity,
                ControlledLuxuries = brain.ControlledLuxuries
            };
            if (brain.CurrentPlan != null)
            {
                data.CurrentPlan = new PlanSaveData
                {
                    PlanID = brain.CurrentPlan.PlanID,
                    Name = brain.CurrentPlan.Name,
                    TargetKingdom = brain.CurrentPlan.TargetKingdom,
                    TargetCity = brain.CurrentPlan.TargetCity,
                    Description = brain.CurrentPlan.Description,
                    TurnStarted = brain.CurrentPlan.TurnStarted,
                    TargetTurns = brain.CurrentPlan.TargetTurns,
                    TurnsElapsed = brain.CurrentPlan.TurnsElapsed,
                    Status = (int)brain.CurrentPlan.Status,
                    StepsCompleted = brain.CurrentPlan.StepsCompleted
                };
            }
            return UnityEngine.JsonUtility.ToJson(data);
        }

        private KingdomBrain DeserializeBrain(string json)
        {
            try
            {
                var data = UnityEngine.JsonUtility.FromJson<BrainSaveData>(json);
                if (data == null) return null;

                var brain = new KingdomBrain(data.BrainID)
                {
                    Personality = (AIPersonality)data.Personality,
                    Ambition = data.Ambition,
                    Memory = data.Memory ?? new List<string>(),
                    LoreHistory = data.LoreHistory ?? new List<string>(),
                    TaxRate = (TaxLevel)data.TaxRate,
                    MilitaryBudget = (BudgetLevel)data.MilitaryBudget,
                    Focus = (NationalFocus)data.Focus,
                    Stance = (MilitaryStance)data.Stance,
                    WarStance = (CombatStance)data.WarStance,
                    CurrentKingID = data.CurrentKingID,
                    SpiesActive = data.SpiesActive,
                    IntelLevel = data.IntelLevel ?? new Dictionary<string, int>(),
                    CityPriorities = (data.CityPriorities ?? new Dictionary<string, int>()).ToDictionary(x => x.Key, x => (CityPriority)x.Value),
                    CompletedPlans = data.CompletedPlans ?? new List<string>(),
                    SiegeTargetCity = data.SiegeTargetCity ?? "",
                    ControlledLuxuries = data.ControlledLuxuries ?? new List<string>()
                };
                if (data.CurrentPlan != null)
                {
                    brain.CurrentPlan = new StrategicPlan
                    {
                        PlanID = data.CurrentPlan.PlanID,
                        Name = data.CurrentPlan.Name,
                        TargetKingdom = data.CurrentPlan.TargetKingdom,
                        TargetCity = data.CurrentPlan.TargetCity,
                        Description = data.CurrentPlan.Description,
                        TurnStarted = data.CurrentPlan.TurnStarted,
                        TargetTurns = data.CurrentPlan.TargetTurns,
                        TurnsElapsed = data.CurrentPlan.TurnsElapsed,
                        Status = (PlanStatus)data.CurrentPlan.Status,
                        StepsCompleted = data.CurrentPlan.StepsCompleted ?? new List<string>()
                    };
                }
                return brain;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIBox] Brain deserialization failed: {e.Message}");
                return null;
            }
        }
    }

    // Serializable save data structures
    [Serializable]
    public class BrainSaveData
    {
        public string BrainID;
        public int Personality;
        public float Ambition;
        public List<string> Memory;
        public List<string> LoreHistory;
        public int TaxRate;
        public int MilitaryBudget;
        public int Focus;
        public int Stance;
        public int WarStance;
        public string CurrentKingID;
        public int SpiesActive;
        public Dictionary<string, int> IntelLevel;
        public Dictionary<string, int> CityPriorities;
        public List<string> CompletedPlans;
        public PlanSaveData CurrentPlan;
        public string SiegeTargetCity;
        public List<string> ControlledLuxuries;
    }

    [Serializable]
    public class PlanSaveData
    {
        public string PlanID;
        public string Name;
        public string TargetKingdom;
        public string TargetCity;
        public string Description;
        public int TurnStarted;
        public int TargetTurns;
        public int TurnsElapsed;
        public int Status;
        public List<string> StepsCompleted;
    }
}
