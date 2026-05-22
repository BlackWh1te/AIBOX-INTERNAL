using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// Per-kingdom economic memory that tracks trends over time.
    /// The AI uses this to understand whether its economy is improving or declining.
    /// All values are REAL game data - no faked numbers.
    /// </summary>
    [Serializable]
    public class EconomicMemory
    {
        // --- Resource History ---
        // Tracks per-resource totals across all cities, sampled every AI tick
        public Dictionary<string, Queue<int>> ResourceHistory = new Dictionary<string, Queue<int>>();
        public const int MAX_HISTORY = 50; // ~4 minutes at 5s interval

        // --- City Snapshots ---
        // Per-city tracked metrics over time
        public Dictionary<string, Queue<CitySnapshot>> CityHistory = new Dictionary<string, Queue<CitySnapshot>>();

        // --- Building History ---
        public Dictionary<string, Queue<BuildingSnapshot>> BuildingHistory = new Dictionary<string, Queue<BuildingSnapshot>>();

        // --- Happiness History ---
        public Queue<float> KingdomAvgHappiness = new Queue<float>();
        public Queue<int> KingdomTotalHungry = new Queue<int>();
        public Queue<int> KingdomTotalHomeless = new Queue<int>();
        public Queue<int> KingdomTotalSick = new Queue<int>();

        // --- Military History ---
        public Queue<int> TotalWarriorsHistory = new Queue<int>();
        public Queue<int> TotalCitiesHistory = new Queue<int>();
        public Queue<int> TotalPopulationHistory = new Queue<int>();

        // --- Spending Tracker ---
        // Tracks what the AI actually spent resources ON (not fake numbers)
        public List<SpendingRecord> SpendingLog = new List<SpendingRecord>();

        // --- Alert Flags ---
        public bool WasRichLastCycle = false;
        public bool WasPoorLastCycle = false;
        public string LastCrisis = "";
        public float LastCrisisTime = 0f;

        [Serializable]
        public struct CitySnapshot
        {
            public float Time;
            public int Population;
            public int Hungry;
            public int Homeless;
            public int Sick;
            public int HousingTotal;
            public int HousingFree;
            public int WarriorsCurrent;
            public int WarriorSlots;
            public int Loyalty;
            public int BuildingsCount;
            public Dictionary<string, int> Resources;
        }

        [Serializable]
        public struct BuildingSnapshot
        {
            public float Time;
            public int TotalBuildings;
            public int Houses;
            public int Barracks;
            public int Farms;
            public int Stockpiles;
            public int Storages;
            public int WatchTowers;
            public int UnderConstruction;
        }

        [Serializable]
        public class SpendingRecord
        {
            public float Time;
            public string Action;
            public string ResourceType;
            public int Amount;
            public string CityName;
            public string Reason;
        }

        /// <summary>
        /// Record a snapshot of the kingdom's real economic state.
        /// Called once per AI cycle.
        /// </summary>
        public void Record(Kingdom k)
        {
            if (k == null || !k.isAlive() || !k.isCiv()) return;

            float now = Time.time;

            // --- Aggregate resources across all cities ---
            var allResources = new Dictionary<string, int>();
            var allCities = k.getCities().ToList();

            foreach (City city in allCities)
            {
                if (city == null || city.isRekt()) continue;
                foreach (var resAsset in AssetManager.resources.list)
                {
                    int amount = city.getResourcesAmount(resAsset.id);
                    if (amount > 0)
                    {
                        if (!allResources.ContainsKey(resAsset.id)) allResources[resAsset.id] = 0;
                        allResources[resAsset.id] += amount;
                    }
                }
            }

            // Push to resource history
            foreach (var kvp in allResources)
            {
                if (!ResourceHistory.ContainsKey(kvp.Key)) ResourceHistory[kvp.Key] = new Queue<int>();
                ResourceHistory[kvp.Key].Enqueue(kvp.Value);
                while (ResourceHistory[kvp.Key].Count > MAX_HISTORY)
                    ResourceHistory[kvp.Key].Dequeue();
            }

            // --- Per-city snapshots ---
            int kingdomHungry = 0;
            int kingdomHomeless = 0;
            int kingdomSick = 0;
            int kingdomWarriors = 0;
            int kingdomPop = 0;

            foreach (City city in allCities)
            {
                if (city == null || city.isRekt()) continue;

                var snap = new CitySnapshot
                {
                    Time = now,
                    Population = city.getPopulationPeople(),
                    Hungry = city.status.hungry,
                    Homeless = city.status.homeless,
                    Sick = city.status.sick,
                    HousingTotal = city.status.housing_total,
                    HousingFree = city.status.housing_free,
                    WarriorsCurrent = city.status.warriors_current,
                    WarriorSlots = city.status.warrior_slots,
                    Loyalty = city.getCachedLoyalty(),
                    BuildingsCount = city.buildings.Count,
                    Resources = new Dictionary<string, int>()
                };

                foreach (var resAsset in AssetManager.resources.list)
                {
                    int amount = city.getResourcesAmount(resAsset.id);
                    if (amount > 0) snap.Resources[resAsset.id] = amount;
                }

                if (!CityHistory.ContainsKey(city.name)) CityHistory[city.name] = new Queue<CitySnapshot>();
                CityHistory[city.name].Enqueue(snap);
                while (CityHistory[city.name].Count > MAX_HISTORY)
                    CityHistory[city.name].Dequeue();

                kingdomHungry += city.status.hungry;
                kingdomHomeless += city.status.homeless;
                kingdomSick += city.status.sick;
                kingdomWarriors += city.status.warriors_current;
                kingdomPop += city.getPopulationPeople();
            }

            // --- Building snapshot ---
            var bSnap = new BuildingSnapshot
            {
                Time = now,
                TotalBuildings = k.buildings.Count,
                Houses = 0,
                Barracks = 0,
                Farms = 0,
                Stockpiles = 0,
                Storages = 0,
                WatchTowers = 0,
                UnderConstruction = 0
            };

            foreach (Building b in k.buildings)
            {
                if (b == null || b.asset == null) continue;
                switch (b.asset.type)
                {
                    case "type_house": bSnap.Houses++; break;
                    case "type_barracks": bSnap.Barracks++; break;
                    case "type_farm": bSnap.Farms++; break;
                    case "type_stockpile": bSnap.Stockpiles++; break;
                    case "type_storage": bSnap.Storages++; break;
                    case "type_watch_tower": bSnap.WatchTowers++; break;
                }
                if (b.isUnderConstruction()) bSnap.UnderConstruction++;
            }

            if (!BuildingHistory.ContainsKey(k.name)) BuildingHistory[k.name] = new Queue<BuildingSnapshot>();
            BuildingHistory[k.name].Enqueue(bSnap);
            while (BuildingHistory[k.name].Count > MAX_HISTORY)
                BuildingHistory[k.name].Dequeue();

            // --- Kingdom-wide aggregates ---
            PushQueue(KingdomTotalHungry, kingdomHungry, MAX_HISTORY);
            PushQueue(KingdomTotalHomeless, kingdomHomeless, MAX_HISTORY);
            PushQueue(KingdomTotalSick, kingdomSick, MAX_HISTORY);
            PushQueue(TotalWarriorsHistory, kingdomWarriors, MAX_HISTORY);
            PushQueue(TotalCitiesHistory, k.countCities(), MAX_HISTORY);
            PushQueue(TotalPopulationHistory, kingdomPop, MAX_HISTORY);

            // Average happiness across kingdom citizens
            float avgHappiness = 0f;
            int citizenCount = 0;
            foreach (Actor a in k.getUnits())
            {
                if (a == null || !a.isAlive()) continue;
                avgHappiness += a.getHappiness();
                citizenCount++;
            }
            if (citizenCount > 0) avgHappiness /= citizenCount;
            PushQueue(KingdomAvgHappiness, avgHappiness, MAX_HISTORY);

            // --- Crisis detection ---
            DetectCrisis(k, allResources, kingdomHungry, kingdomHomeless, avgHappiness);
        }

        private void PushQueue<T>(Queue<T> q, T value, int max)
        {
            q.Enqueue(value);
            while (q.Count > max) q.Dequeue();
        }

        private void DetectCrisis(Kingdom k, Dictionary<string, int> allResources, int hungry, int homeless, float avgHappiness)
        {
            float now = Time.time;
            string crisis = "";

            int pop = TotalPopulationHistory.Count > 0 ? TotalPopulationHistory.Last() : 0;
            if (pop > 0)
            {
                if (hungry > pop / 4) crisis += "MASS_HUNGER ";
                if (homeless > pop / 5) crisis += "MASS_HOMELESSNESS ";
            }
            if (avgHappiness < -30)
                crisis += "LOW_MORALE ";
            if (k.countCities() > 1 && TotalCitiesHistory.Count >= 2 && TotalCitiesHistory.Last() < TotalCitiesHistory.ElementAt(TotalCitiesHistory.Count - 2))
                crisis += "LOST_CITY ";

            if (!string.IsNullOrEmpty(crisis) && crisis != LastCrisis && (now - LastCrisisTime) > 30f)
            {
                LastCrisis = crisis;
                LastCrisisTime = now;
            }
        }

        // --- Trend Analysis Helpers ---

        public string GetResourceTrend(string resourceId)
        {
            if (!ResourceHistory.ContainsKey(resourceId) || ResourceHistory[resourceId].Count < 3)
                return "insufficient data";

            var vals = ResourceHistory[resourceId].ToArray();
            int recent = vals[vals.Length - 1];
            int old = vals[0];
            int change = recent - old;
            float rate = (float)change / Mathf.Max(1, vals.Length);

            if (change > 20) return $"rising fast (+{change}, +{rate:F1}/tick)";
            if (change > 5) return $"rising (+{change})";
            if (change < -20) return $"falling fast ({change}, {rate:F1}/tick)";
            if (change < -5) return $"falling ({change})";
            return "stable";
        }

        public string GetPopulationTrend()
        {
            if (TotalPopulationHistory.Count < 3) return "insufficient data";
            var vals = TotalPopulationHistory.ToArray();
            int change = vals[vals.Length - 1] - vals[0];
            if (change > 20) return $"booming (+{change})";
            if (change > 5) return $"growing (+{change})";
            if (change < -20) return $"collapsing ({change})";
            if (change < -5) return $"shrinking ({change})";
            return "stable";
        }

        public string GetMilitaryTrend()
        {
            if (TotalWarriorsHistory.Count < 3) return "insufficient data";
            var vals = TotalWarriorsHistory.ToArray();
            int change = vals[vals.Length - 1] - vals[0];
            if (change > 10) return $"strengthening (+{change})";
            if (change < -10) return $"weakening ({change})";
            return "stable";
        }

        public string GetSpendingSummary(float sinceTime)
        {
            if (SpendingLog.Count == 0) return "No spending records.";
            var recent = SpendingLog.Where(r => r.Time >= sinceTime).ToList();
            if (recent.Count == 0) return "No recent spending.";

            var byAction = new Dictionary<string, int>();
            foreach (var r in recent)
            {
                string key = $"{r.Action} ({r.ResourceType})";
                if (!byAction.ContainsKey(key)) byAction[key] = 0;
                byAction[key] += r.Amount;
            }

            string summary = "";
            foreach (var kvp in byAction.OrderByDescending(x => x.Value).Take(5))
                summary += $"{kvp.Key}: {kvp.Value}; ";
            return summary;
        }

        public void LogSpending(string action, string resourceType, int amount, string cityName, string reason)
        {
            SpendingLog.Add(new SpendingRecord
            {
                Time = Time.time,
                Action = action,
                ResourceType = resourceType,
                Amount = amount,
                CityName = cityName,
                Reason = reason
            });
            if (SpendingLog.Count > 200) SpendingLog.RemoveAt(0);
        }

        public string GetFullTrendReport(Kingdom k)
        {
            string report = $"=== ECONOMIC TREND REPORT FOR {k.name} ===\n";
            report += $"Population: {GetPopulationTrend()} (current: {(TotalPopulationHistory.Count > 0 ? TotalPopulationHistory.Last() : 0)})\n";
            report += $"Military: {GetMilitaryTrend()} (current: {(TotalWarriorsHistory.Count > 0 ? TotalWarriorsHistory.Last() : 0)})\n";
            report += $"Avg Happiness: {(KingdomAvgHappiness.Count > 0 ? KingdomAvgHappiness.Last() : 0):F0}\n";
            report += $"Hungry Citizens: {(KingdomTotalHungry.Count > 0 ? KingdomTotalHungry.Last() : 0)}\n";
            report += $"Homeless: {(KingdomTotalHomeless.Count > 0 ? KingdomTotalHomeless.Last() : 0)}\n";
            report += $"Cities: {(TotalCitiesHistory.Count > 0 ? TotalCitiesHistory.Last() : 0)}\n";
            report += "\nResource Trends:\n";
            foreach (var res in ResourceHistory.Keys.OrderBy(x => x))
            {
                report += $"  {res}: {GetResourceTrend(res)} (current: {ResourceHistory[res].Last()})\n";
            }
            if (!string.IsNullOrEmpty(LastCrisis) && (Time.time - LastCrisisTime) < 60f)
                report += $"\nACTIVE CRISIS: {LastCrisis} (since {Time.time - LastCrisisTime:F0}s ago)\n";
            report += "=== END REPORT ===";
            return report;
        }
    }
}
