using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// Tracks state changes and incoming events between AI cycles.
    /// Generates a "morning inbox" report so the AI knows what happened while it was away.
    ///
    /// Three categories of events:
    ///   YOU:      Actions our AI ordered and their results
    ///   GAME:     Native game events (succession, rebellions, plots succeeding)
    ///   KINGDOM:  Actions by OTHER kingdoms directed AT us (war declarations, mail, etc.)
    /// </summary>
    public class KingdomEventTracker
    {
        // --- Pending Action Tracking ---
        public class PendingAction
        {
            public string Id;
            public string ActionType;
            public string Target;
            public float OrderedAt;
            public ActionStatus Status;
            public string ResultMessage;
            public int AttemptCount;
        }

        public enum ActionStatus { Pending, InProgress, Succeeded, Failed, Cancelled }

        public List<PendingAction> PendingActions = new List<PendingAction>();

        // --- State Snapshots for Diff Detection ---
        [Serializable]
        public class StateSnapshot
        {
            public float Timestamp;
            public List<string> WarNames = new List<string>();
            public string KingName;
            public List<string> CityNames = new List<string>();
            public bool HasAlliance;
            public string AllianceName;
            public int CityCount;
            public int Population;
            public int ArmySize;
            public int TotalGold;
            public List<string> EnemyKingdoms = new List<string>();
            public List<string> AlliedKingdoms = new List<string>();
        }

        private StateSnapshot _lastSnapshot;

        // --- Event Log ---
        public class GameEvent
        {
            public float Timestamp;
            public string Category;    // "WAR", "PEACE", "SUCCESSION", "REBELLION", "ALLIANCE", "PLOT", "MAIL", "CITY_LOST", "CITY_GAINED", "TRADE"
            public string Source;      // "YOU", "GAME", or "KINGDOM:Name"
            public string Description;
            public bool Resolved;      // For pending actions: has this been resolved?
        }

        public List<GameEvent> RecentEvents = new List<GameEvent>();
        private const int MAX_EVENTS = 30;
        private const float EVENT_TTL = 120f; // Events expire after 2 minutes real time

        // --- Action Registration ---

        /// <summary>
        /// Call this immediately when our AI decides an action.
        /// </summary>
        public void RecordOurAction(string actionType, string target, string actionId = null)
        {
            var pending = new PendingAction
            {
                Id = actionId ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                ActionType = actionType,
                Target = target ?? "",
                OrderedAt = Time.time,
                Status = ActionStatus.Pending,
                ResultMessage = "",
                AttemptCount = 1
            };
            PendingActions.Add(pending);

            // Also add as an event so the AI sees "I ordered X"
            AddEvent("ACTION", "YOU", $"You ordered: {actionType}{(string.IsNullOrEmpty(target) ? "" : " ~ " + target)}");
        }

        /// <summary>
        /// Call this after checking if the action succeeded.
        /// </summary>
        public void ResolveAction(string actionType, string target, bool succeeded, string reason = "")
        {
            var pending = PendingActions.FirstOrDefault(p =>
                p.ActionType == actionType &&
                (string.IsNullOrEmpty(target) || p.Target == target) &&
                p.Status == ActionStatus.Pending);

            if (pending != null)
            {
                pending.Status = succeeded ? ActionStatus.Succeeded : ActionStatus.Failed;
                pending.ResultMessage = reason;
                AddEvent("ACTION_RESULT", "YOU",
                    $"Your {actionType}{(string.IsNullOrEmpty(target) ? "" : " on " + target)} → {(succeeded ? "SUCCEEDED" : "FAILED")}. {reason}".Trim());
            }
            else
            {
                // Action wasn't tracked as pending — add a generic result
                AddEvent("ACTION_RESULT", "YOU",
                    $"{actionType}{(string.IsNullOrEmpty(target) ? "" : " on " + target)} → {(succeeded ? "SUCCEEDED" : "FAILED")}. {reason}".Trim());
            }

            // Clean up old resolved actions
            PendingActions.RemoveAll(p => p.Status == ActionStatus.Succeeded || p.Status == ActionStatus.Failed || p.Status == ActionStatus.Cancelled);
        }

        /// <summary>
        /// Call this to mark a pending action as still in progress.
        /// </summary>
        public void UpdateActionProgress(string actionType, string target, string progressNote)
        {
            var pending = PendingActions.FirstOrDefault(p =>
                p.ActionType == actionType &&
                (string.IsNullOrEmpty(target) || p.Target == target) &&
                p.Status == ActionStatus.Pending);

            if (pending != null)
            {
                pending.Status = ActionStatus.InProgress;
                pending.AttemptCount++;
                AddEvent("ACTION_PROGRESS", "YOU",
                    $"Your {actionType} on {target} is IN PROGRESS: {progressNote}");
            }
        }

        // --- State Diff & Event Detection ---

        /// <summary>
        /// Call at the START of an AI cycle, BEFORE the AI thinks.
        /// Compares current world state to the saved snapshot and generates events.
        /// </summary>
        public List<GameEvent> DetectEvents(Kingdom k)
        {
            var newEvents = new List<GameEvent>();
            if (k == null || !k.isAlive()) return newEvents;

            var current = CaptureSnapshot(k);
            if (_lastSnapshot == null)
            {
                _lastSnapshot = current;
                return newEvents; // First cycle — nothing to compare
            }

            // --- WAR CHANGES ---
            var lastWars = new HashSet<string>(_lastSnapshot.WarNames);
            var currentWars = new HashSet<string>(current.WarNames);

            // Wars that started since last cycle
            foreach (var warName in currentWars.Except(lastWars))
            {
                // Find the war object to determine who started it
                var war = World.world.wars.getWars(k)?.FirstOrDefault(w => w.name == warName);
                if (war != null)
                {
                    string otherSide = GetOtherSide(war, k);
                    bool weStarted = war.isMainAttacker(k);
                    string source = weStarted ? "YOU" : (string.IsNullOrEmpty(otherSide) ? "GAME" : $"KINGDOM:{otherSide}");
                    string desc = weStarted
                        ? $"You declared war on {otherSide}!"
                        : $"{otherSide} declared war on us!";
                    newEvents.Add(MakeEvent("WAR", source, desc));
                }
                else
                {
                    newEvents.Add(MakeEvent("WAR", "GAME", $"War started: {warName}"));
                }
            }

            // Wars that ended since last cycle
            foreach (var warName in lastWars.Except(currentWars))
            {
                newEvents.Add(MakeEvent("PEACE", "GAME", "A war has ended (peace, surrender, or merge)."));
            }

            // --- SUCCESSION ---
            if (!string.IsNullOrEmpty(_lastSnapshot.KingName) && _lastSnapshot.KingName != current.KingName)
            {
                if (!string.IsNullOrEmpty(_lastSnapshot.KingName) && _lastSnapshot.KingName != "None")
                {
                    newEvents.Add(MakeEvent("SUCCESSION", "GAME",
                        $"King {_lastSnapshot.KingName} is no longer ruler. New king: {current.KingName}"));
                }
                else
                {
                    newEvents.Add(MakeEvent("SUCCESSION", "GAME", $"New king crowned: {current.KingName}"));
                }
            }

            // --- CITY CHANGES ---
            var lastCities = new HashSet<string>(_lastSnapshot.CityNames);
            var currentCities = new HashSet<string>(current.CityNames);

            // Cities lost
            foreach (var lostCity in lastCities.Except(currentCities))
            {
                newEvents.Add(MakeEvent("CITY_LOST", "GAME", $"We lost control of city '{lostCity}' (rebellion or conquest)."));
            }

            // Cities gained
            foreach (var gainedCity in currentCities.Except(lastCities))
            {
                newEvents.Add(MakeEvent("CITY_GAINED", "GAME", $"We gained control of city '{gainedCity}' (expansion or conquest)."));
            }

            // --- ALLIANCE CHANGES ---
            if (_lastSnapshot.HasAlliance != current.HasAlliance)
            {
                if (current.HasAlliance)
                    newEvents.Add(MakeEvent("ALLIANCE", "GAME", $"We joined an alliance{(string.IsNullOrEmpty(current.AllianceName) ? "" : ": " + current.AllianceName)}."));
                else
                    newEvents.Add(MakeEvent("ALLIANCE", "GAME", "We left our alliance (or it dissolved)."));
            }

            // --- POPULATION COLLAPSE / BOOM ---
            if (_lastSnapshot.Population > 0 && current.Population < _lastSnapshot.Population * 0.8f)
            {
                int loss = _lastSnapshot.Population - current.Population;
                newEvents.Add(MakeEvent("POPULATION", "GAME", $"Population dropped sharply! Lost {loss} people (disease, famine, or war)."));
            }

            // --- ARMY COLLAPSE ---
            if (_lastSnapshot.ArmySize > 5 && current.ArmySize < _lastSnapshot.ArmySize * 0.5f)
            {
                int loss = _lastSnapshot.ArmySize - current.ArmySize;
                newEvents.Add(MakeEvent("MILITARY", "GAME", $"Army collapsed! Lost {loss} warriors in battle."));
            }

            // --- GOLD DRAIN ---
            if (_lastSnapshot.TotalGold > 100 && current.TotalGold < _lastSnapshot.TotalGold * 0.3f)
            {
                int loss = _lastSnapshot.TotalGold - current.TotalGold;
                newEvents.Add(MakeEvent("ECONOMY", "GAME", $"Treasury drained! Lost {loss} gold."));
            }

            // Save snapshot for next cycle
            _lastSnapshot = current;

            // Merge new events into RecentEvents
            foreach (var evt in newEvents)
                RecentEvents.Add(evt);

            CleanupOldEvents();
            return newEvents;
        }

        /// <summary>
        /// Records an incoming mail event so the AI sees "You got mail from X"
        /// Call this when MailRegistry detects new unread mail for this kingdom.
        /// </summary>
        public void RecordIncomingMail(string fromKingdom, string subject, string bodyPreview)
        {
            string preview = bodyPreview.Length > 60 ? bodyPreview.Substring(0, 60) + "..." : bodyPreview;
            AddEvent("MAIL", $"KINGDOM:{fromKingdom}", $"Mail from {fromKingdom}: \"{subject}\" — \"{preview}\"");
        }

        /// <summary>
        /// Records that another kingdom declared war on us (detected via state diff, but
        /// this can be called explicitly if we intercept it via patches).
        /// </summary>
        public void RecordIncomingWar(string fromKingdom, string reasonHint = "")
        {
            string desc = string.IsNullOrEmpty(reasonHint)
                ? $"{fromKingdom} declared war on us!"
                : $"{fromKingdom} declared war on us! ({reasonHint})";
            AddEvent("WAR", $"KINGDOM:{fromKingdom}", desc);
        }

        /// <summary>
        /// Records a native game event explicitly (e.g. from a Harmony patch).
        /// </summary>
        public void RecordGameEvent(string category, string description)
        {
            AddEvent(category, "GAME", description);
        }

        /// <summary>
        /// Records an espionage/intelligence event.
        /// </summary>
        public void RecordEspionage(string targetKingdom, string intelSummary, int intelLevel)
        {
            AddEvent("ESPIONAGE", "YOU", $"Spy report from {targetKingdom}: {intelSummary} (Intel level: {intelLevel})");
        }

        /// <summary>
        /// Records that a spy was captured by another kingdom.
        /// </summary>
        public void RecordSpyCaptured(string byKingdom, string outcome)
        {
            AddEvent("ESPIONAGE", $"KINGDOM:{byKingdom}", $"Our spy was captured by {byKingdom}! {outcome}");
        }

        /// <summary>
        /// Records a trade caravan event.
        /// </summary>
        public void RecordTrade(string targetKingdom, bool succeeded, string details)
        {
            string status = succeeded ? "succeeded" : "failed";
            AddEvent("TRADE", succeeded ? "YOU" : "GAME",
                $"Trade caravan to {targetKingdom} {status}. {details}");
        }

        /// <summary>
        /// Records that a plot was started by our king (or detected from another kingdom).
        /// </summary>
        public void RecordPlotStarted(string plotType, string target, bool isOurPlot)
        {
            string source = isOurPlot ? "YOU" : $"KINGDOM:{target}";
            string desc = isOurPlot
                ? $"Our king started a '{plotType}' plot against {target}."
                : $"Intelligence suggests {target} started a '{plotType}' plot.";
            AddEvent("PLOT", source, desc);
        }

        /// <summary>
        /// Records a plot outcome.
        /// </summary>
        public void RecordPlotOutcome(string plotType, string target, bool succeeded, string result)
        {
            string status = succeeded ? "SUCCEEDED" : "FAILED";
            AddEvent("PLOT_RESULT", "GAME",
                $"Plot '{plotType}' against {target} → {status}. {result}");
        }

        /// <summary>
        /// Records a city-specific event (siege, riot, festival, etc.).
        /// </summary>
        public void RecordCityEvent(string cityName, string eventType, string description)
        {
            AddEvent("CITY", "GAME", $"City '{cityName}': {eventType}. {description}");
        }

        /// <summary>
        /// Records a settlement/conquest event.
        /// </summary>
        public void RecordSettlement(string siteName, bool succeeded)
        {
            string status = succeeded ? "Settlement established" : "Settlement failed";
            AddEvent("SETTLEMENT", "YOU", $"{status} at {siteName}.");
        }

        // --- Snapshot Capture ---

        private StateSnapshot CaptureSnapshot(Kingdom k)
        {
            var snap = new StateSnapshot
            {
                Timestamp = Time.time,
                KingName = k.king != null ? k.king.getName() : "None",
                HasAlliance = k.hasAlliance(),
                AllianceName = k.hasAlliance() ? k.getAlliance()?.name : "",
                CityCount = k.countCities(),
                Population = k.getPopulationPeople(),
                ArmySize = k.countTotalWarriors(),
                TotalGold = 0
            };

            foreach (City c in k.getCities())
            {
                if (c == null || c.isRekt()) continue;
                snap.CityNames.Add(c.name);
                snap.TotalGold += c.getResourcesAmount("gold");
            }

            var wars = World.world.wars.getWars(k);
            if (wars != null)
            {
                foreach (var war in wars)
                {
                    if (war == null) continue;
                    snap.WarNames.Add(war.name);

                    // Track enemies from wars
                    foreach (var attacker in war.getAttackers())
                    {
                        if (attacker != k && attacker != null && !snap.EnemyKingdoms.Contains(attacker.name))
                            snap.EnemyKingdoms.Add(attacker.name);
                    }
                    foreach (var defender in war.getDefenders())
                    {
                        if (defender != k && defender != null && !snap.EnemyKingdoms.Contains(defender.name))
                            snap.EnemyKingdoms.Add(defender.name);
                    }
                }
            }

            // Track allies
            if (k.hasAlliance())
            {
                var alliance = k.getAlliance();
                if (alliance != null)
                {
                    foreach (var ally in alliance.kingdoms_hashset)
                    {
                        if (ally != k && ally != null && !snap.AlliedKingdoms.Contains(ally.name))
                            snap.AlliedKingdoms.Add(ally.name);
                    }
                }
            }

            return snap;
        }

        // --- Helpers ---

        private GameEvent MakeEvent(string category, string source, string description)
        {
            return new GameEvent
            {
                Timestamp = Time.time,
                Category = category,
                Source = source,
                Description = description,
                Resolved = false
            };
        }

        private void AddEvent(string category, string source, string description)
        {
            RecentEvents.Add(MakeEvent(category, source, description));
            if (RecentEvents.Count > MAX_EVENTS)
                RecentEvents.RemoveAt(0);
        }

        private void CleanupOldEvents()
        {
            float now = Time.time;
            RecentEvents.RemoveAll(e => now - e.Timestamp > EVENT_TTL);
        }

        private string GetOtherSide(War war, Kingdom us)
        {
            try
            {
                if (war.isAttacker(us))
                {
                    var defender = war.getDefenders().FirstOrDefault(d => d != null);
                    return defender != null ? defender.name : "an enemy";
                }
                else
                {
                    var attacker = war.getAttackers().FirstOrDefault(a => a != null);
                    return attacker != null ? attacker.name : "an enemy";
                }
            }
            catch { return "an enemy"; }
        }

        // --- String Builder for Prompt ---

        /// <summary>
        /// Builds the "morning inbox" section for the AI prompt.
        /// Shows events from newest to oldest, grouped by category.
        /// </summary>
        public string BuildEventLogString(int maxEvents = 10)
        {
            if (RecentEvents.Count == 0 && PendingActions.Count == 0)
                return "";

            string result = "\n=== EVENTS SINCE YOUR LAST TURN ===\n";

            // 1. Pending actions first (most important)
            var stillPending = PendingActions.Where(p => p.Status == ActionStatus.Pending || p.Status == ActionStatus.InProgress).ToList();
            if (stillPending.Count > 0)
            {
                result += "[PENDING ACTIONS]\n";
                foreach (var p in stillPending.Take(3))
                {
                    string status = p.Status == ActionStatus.InProgress ? "IN PROGRESS" : "PENDING";
                    result += $"  {status}: {p.ActionType} {(string.IsNullOrEmpty(p.Target) ? "" : "~ " + p.Target)}\n";
                }
            }

            // 2. Recent events grouped by source
            var eventsToShow = RecentEvents.OrderByDescending(e => e.Timestamp).Take(maxEvents).ToList();
            if (eventsToShow.Count > 0)
            {
                result += "[WHAT HAPPENED]\n";
                foreach (var evt in eventsToShow)
                {
                    string prefix;
                    switch (evt.Source)
                    {
                        case "YOU": prefix = "[YOU]"; break;
                        case "GAME": prefix = "[GAME]"; break;
                        default:
                            if (evt.Source.StartsWith("KINGDOM:"))
                                prefix = $"[{evt.Source.Replace("KINGDOM:", "FROM ")}]";
                            else
                                prefix = $"[{evt.Source}]";
                            break;
                    }
                    result += $"  {prefix} {evt.Description}\n";
                }
            }

            return result;
        }

        /// <summary>
        /// Call when the world changes (new game loaded) to clear stale data.
        /// </summary>
        public void Reset()
        {
            _lastSnapshot = null;
            PendingActions.Clear();
            RecentEvents.Clear();
        }
    }
}
