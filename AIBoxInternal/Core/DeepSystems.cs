using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    // NOTE: Old fake EconomySim has been REMOVED.
    // The AI now reads real prices from the game if any exist, or ignores market manipulation.

    public enum WorldPhase { Stable, Tense, TotalWar }

    public enum AIPersonality { Expansionist, Pacifist, Merchant, Tyrant }

    [Serializable]
    public struct CityEconomicState
    {
        public string Name;
        public int Gold;
        public int Food;
        public bool IsDistressed;
        public string LuxuryResource;
    }

    public enum TaxLevel { Low, Medium, High }
    public enum BudgetLevel { Defensive, Balanced, Aggressive }
    public enum NationalFocus { Expansion, Economic, Cultural, Military }
    public enum MilitaryStance { Balanced, Blitzkrieg, Guerrilla, ScorchedEarth }
    public enum MissionType { Espionage, Trade, Settlement }
    public enum CityPriority { Balanced, Military, Economy, Growth, FoodFirst, Housing }
    public enum CombatStance { Aggressive, Balanced, Defensive, Retreat }

    [Serializable]
    public class StrategicPlan
    {
        public string PlanID;
        public string Name;
        public int TurnStarted;
        public int TargetTurns;
        public int TurnsElapsed;
        public PlanStatus Status;
        public string TargetKingdom;
        public string TargetCity;
        public string Description;
        public List<string> StepsCompleted = new List<string>();
        public string NextStep => GetNextStep();

        private string GetNextStep()
        {
            if (Status != PlanStatus.Active) return "COMPLETE";
            if (Name.Contains("CONQUER")) return TurnsElapsed < 2 ? "MOBILIZE" : TurnsElapsed < 4 ? "SIEGE" : "OCCUPY";
            if (Name.Contains("DEFEND")) return TurnsElapsed < 2 ? "FORTIFY" : "HOLD";
            if (Name.Contains("ECONOMY")) return TurnsElapsed < 2 ? "TRADE" : TurnsElapsed < 4 ? "BUILD" : "EXPAND";
            return "CONTINUE";
        }
    }

    public enum PlanStatus { Active, Completed, Failed, Cancelled }

    [Serializable]
    public class ActiveMission
    {
        public string TargetKingdom;
        public string TargetCity;
        public MissionType Type;
        public string Detail;
        public float Progress;
        public string ActorID;
    }

    [Serializable]
    public class SurveyPoint
    {
        public int TileIndex;
        public string Description;
    }

    [Serializable]
    public class ActionRecord
    {
        public float Timestamp;
        public string Action;
        public string Result;
        public int PopulationBefore;
        public int PopulationAfter;
        public int CitiesBefore;
        public int CitiesAfter;
        public int ArmyBefore;
        public int ArmyAfter;
        public int GoldBefore;
        public int GoldAfter;
    }

    public class KingdomBrain
    {
        public AIPersonality Personality;
        public float Ambition;
        public List<string> Memory = new List<string>();
        public List<string> LoreHistory = new List<string>();
        public List<ActionRecord> ActionHistory = new List<ActionRecord>();

        // --- Ruler Settings ---
        public TaxLevel TaxRate = TaxLevel.Medium;
        public BudgetLevel MilitaryBudget = BudgetLevel.Balanced;
        public NationalFocus Focus = NationalFocus.Economic;
        public MilitaryStance Stance = MilitaryStance.Balanced;

        // --- Real Economic Memory (not fake numbers) ---
        public EconomicMemory MemoryBank = new EconomicMemory();

        // --- Legacy fields kept for compatibility, but now read-only from game ---
        public float Treasury => GetRealTreasury();
        public string LastThink = "The realm is stable.";
        public string LastAction = "WAITING";

        public KingdomConfig Config = new KingdomConfig();
        public List<string> MailLogs = new List<string>();
        public List<string> IncomingMail = new List<string>();
        public List<ActiveMission> ActiveMissions = new List<ActiveMission>();
        public Dictionary<string, SurveyPoint> PendingSurveys = new Dictionary<string, SurveyPoint>();
        public Dictionary<string, CityEconomicState> CityData = new Dictionary<string, CityEconomicState>();
        public List<string> ControlledLuxuries = new List<string>();
        public string CurrentKingID = "";
        public int SpiesActive = 0;
        public Dictionary<string, int> IntelLevel = new Dictionary<string, int>();

        public List<string> ChatHistory = new List<string>();

        /// <summary>
        /// Tracks state changes and incoming events between AI cycles.
        /// Generates the "morning inbox" report so the AI knows what happened while away.
        /// </summary>
        public KingdomEventTracker EventTracker = new KingdomEventTracker();

        // --- City-Level Priorities ---
        public Dictionary<string, CityPriority> CityPriorities = new Dictionary<string, CityPriority>();

        // --- Multi-Turn Strategic Planning ---
        public StrategicPlan CurrentPlan = null;
        public List<string> CompletedPlans = new List<string>();

        // --- Combat State ---
        public CombatStance WarStance = CombatStance.Aggressive;
        public string SiegeTargetCity = "";

        // --- Save/Load Persistence ID ---
        public string BrainID = System.Guid.NewGuid().ToString("N");

        public KingdomBrain()
        {
            Personality = (AIPersonality)UnityEngine.Random.Range(0, 4);
            Ambition = UnityEngine.Random.Range(0.3f, 1.0f);
        }

        public KingdomBrain(string persistentID)
        {
            BrainID = persistentID;
            Personality = (AIPersonality)UnityEngine.Random.Range(0, 4);
            Ambition = UnityEngine.Random.Range(0.3f, 1.0f);
        }

        /// <summary>
        /// Sum real gold across all cities in the kingdom.
        /// </summary>
        private float GetRealTreasury()
        {
            if (RealTimeDB.Kingdoms == null) return 0;
            // We don't have a direct kingdom reference here, but MemoryBank can track it
            // For now, return 0 - the prompt uses RealTimeDB directly for gold
            return 0;
        }

        public ActionRecord BeginAction(string actionName, Kingdom k)
        {
            return new ActionRecord
            {
                Timestamp = Time.time,
                Action = actionName,
                PopulationBefore = k.getPopulationPeople(),
                CitiesBefore = k.countCities(),
                ArmyBefore = k.countTotalWarriors(),
                GoldBefore = GetKingdomGold(k)
            };
        }

        public void EndAction(ActionRecord record, Kingdom k, string result)
        {
            record.PopulationAfter = k.getPopulationPeople();
            record.CitiesAfter = k.countCities();
            record.ArmyAfter = k.countTotalWarriors();
            record.GoldAfter = GetKingdomGold(k);
            record.Result = result;

            ActionHistory.Add(record);
            if (ActionHistory.Count > 50) ActionHistory.RemoveAt(0);
        }

        private int GetKingdomGold(Kingdom k)
        {
            int total = 0;
            foreach (City c in k.getCities())
            {
                if (c != null && !c.isRekt())
                    total += c.getResourcesAmount("gold");
            }
            return total;
        }

        public string GetRecentHistorySummary()
        {
            if (ActionHistory.Count == 0) return "No recent actions.";
            var recent = ActionHistory.TakeLast(5).ToList();
            string summary = "";
            foreach (var r in recent)
            {
                string popChange = r.PopulationAfter != r.PopulationBefore
                    ? $" (pop: {r.PopulationBefore}->{r.PopulationAfter})" : "";
                string cityChange = r.CitiesAfter != r.CitiesBefore
                    ? $" (cities: {r.CitiesBefore}->{r.CitiesAfter})" : "";
                string armyChange = r.ArmyAfter != r.ArmyBefore
                    ? $" (army: {r.ArmyBefore}->{r.ArmyAfter})" : "";
                string goldChange = r.GoldAfter != r.GoldBefore
                    ? $" (gold: {r.GoldBefore}->{r.GoldAfter})" : "";
                summary += $"[{r.Action}] {r.Result}{popChange}{cityChange}{armyChange}{goldChange}. ";
            }
            return summary.Trim();
        }

        /// <summary>
        /// The AI's internal decision engine when no external LLM is used.
        /// Now makes decisions based on REAL game state instead of fake numbers.
        /// </summary>
        public string DecideAction(string context, Kingdom k)
        {
            if (GlobalState.PendingWhispers.ContainsKey(k.name))
            {
                string whisper = GlobalState.PendingWhispers[k.name];
                GlobalState.PendingWhispers.Remove(k.name);
                Memory.Add($"HEARD DIVINE WHISPER: {whisper}");

                try
                {
                    var msg = new WorldLogMessage(WorldLogLibrary.king_new, k.name, "heard a divine whisper: " + whisper, null);
                    msg.kingdom = k;
                    if (k.capital != null) msg.location = k.capital.last_city_center;
                    msg.color_special1 = k.getColor().getColorText();
                    msg.add();
                }
                catch (Exception e) { Debug.LogWarning($"[AIBox] WorldLog whisper failed: {e.Message}"); }

                return "EXECUTE_WHISPER_" + whisper.ToUpper().Replace(" ", "_");
            }

            // Read real state from snapshot if available
            int realGold = 0;
            int realFood = 0;
            int realPop = k.getPopulationPeople();
            int realArmy = k.countTotalWarriors();
            int realCities = k.countCities();
            bool hasHungry = false;
            bool hasHomeless = false;
            bool lowLoyalty = false;
            bool undermannedArmy = false;

            if (RealTimeDB.Kingdoms.TryGetValue(k.name, out var snap))
            {
                realGold = snap.TotalGold;
                realFood = snap.TotalFood;
                hasHungry = snap.TotalHungry > snap.Population / 5;
                hasHomeless = snap.TotalHomeless > snap.Population / 5;
                lowLoyalty = snap.LowestLoyalty < -20;
                undermannedArmy = snap.TotalWarriorSlots > 0 && snap.ArmySize < snap.TotalWarriorSlots / 2;
            }

            // EMERGENCY overrides based on real conditions
            if (hasHungry && realFood < 20 && realGold > 100)
                return "FOCUS_FOOD"; // Signal to AI that food is critical
            if (hasHomeless)
                return "FOCUS_HOUSING"; // Signal housing crisis
            if (lowLoyalty && realGold > 200)
                return "FESTIVAL"; // Try to boost morale if we can afford it
            if (undermannedArmy && realGold > 300)
                return "HIRE_MERCENARIES";

            // Personality-based decisions
            switch (Personality)
            {
                case AIPersonality.Expansionist:
                    if (realArmy > 20 && Ambition > 0.5f && realCities < 5) return "DECLARE_WAR";
                    if (realArmy > 10 && realGold > 200) return "HIRE_MERCENARIES";
                    if (SpiesActive > 0) return "SABOTAGE_ENEMY";
                    return "SURVEY_LAND";

                case AIPersonality.Merchant:
                    if (realCities > 1 && realGold > 100) return "START_TRADE:" + GetRichestNeighbor(k);
                    return "STRENGTHEN_CULTURE";

                case AIPersonality.Pacifist:
                    if (realCities > 0 && realGold > 300) return "FESTIVAL";
                    if (Ambition > 0.7f) return "FORM_ALLIANCE:" + GetStrongestNeighbor(k);
                    return "STRENGTHEN_CULTURE";

                case AIPersonality.Tyrant:
                    if (Ambition > 0.4f) return "ASSASSINATE_CHIEF:" + GetWeakestNeighbor(k);
                    return "SABOTAGE_ENEMY";

                default:
                    return "STAY_STILL";
            }
        }

        private string GetRichestNeighbor(Kingdom k)
        {
            Kingdom best = null; int bestGold = 0;
            foreach (var other in World.world.kingdoms.list)
            {
                if (other == k || !other.isAlive() || !other.isCiv()) continue;
                int gold = Core.RealTimeDB.Kingdoms.ContainsKey(other.name) ? Core.RealTimeDB.Kingdoms[other.name].TotalGold : 0;
                if (gold > bestGold) { bestGold = gold; best = other; }
            }
            return best != null ? best.name : "";
        }

        private string GetStrongestNeighbor(Kingdom k)
        {
            Kingdom best = null; int bestArmy = 0;
            foreach (var other in World.world.kingdoms.list)
            {
                if (other == k || !other.isAlive() || !other.isCiv()) continue;
                int army = other.countTotalWarriors();
                if (army > bestArmy) { bestArmy = army; best = other; }
            }
            return best != null ? best.name : "";
        }

        private string GetWeakestNeighbor(Kingdom k)
        {
            Kingdom worst = null; int worstArmy = int.MaxValue;
            foreach (var other in World.world.kingdoms.list)
            {
                if (other == k || !other.isAlive() || !other.isCiv()) continue;
                int army = other.countTotalWarriors();
                if (army < worstArmy) { worstArmy = army; worst = other; }
            }
            return worst != null ? worst.name : "";
        }
    }

    public static class GlobalState
    {
        public static Dictionary<string, string> PendingWhispers = new Dictionary<string, string>();
        public static List<string> GlobalNews = new List<string>();
        public static float WorldTension = 0f;
        public static WorldPhase CurrentPhase = WorldPhase.Stable;
        public static string[] LuxuryTypes = { "Silk", "Spice", "Gems", "Incense", "Ivory" };
    }
}
