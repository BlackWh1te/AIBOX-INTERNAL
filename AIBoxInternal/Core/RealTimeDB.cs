using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// RealTimeDB is the single source of truth for all in-game kingdom data.
    /// It is refreshed every AI cycle BEFORE any AI thinking occurs.
    /// The AI prompt is built exclusively from this data, ensuring no hallucinated values.
    /// </summary>
    public static class RealTimeDB
    {
        /// <summary>
        /// Per-kingdom snapshot of all real game data.
        /// </summary>
        public class KingdomSnapshot
        {
            public string Name;
            public string RulerName;
            public int Population;
            public int ArmySize;
            public int CityCount;
            public int TotalGold;
            public int TotalFood;
            public int TotalWood;
            public int TotalIron;
            public int TotalStone;
            public List<string> CityNames = new List<string>();
            public List<string> ActiveWars = new List<string>();
            public List<string> AlliedKingdoms = new List<string>();
            public string Culture;
            public string Religion;
            public List<string> CultureTraits = new List<string>();
            public string CapitalBiome;

            // Ruler stats
            public int KingIntelligence;
            public int KingDiplomacy;
            public int KingWarfare;
            public int KingStewardship;
            public int KingRenown;
            public int KingLevel;
            public bool KingHasPlot;
            public string KingPlotType;

            // Active plots in the kingdom (author name -> plot type)
            public List<string> ActivePlots = new List<string>();

            // Kingdom-wide aggregates
            public int TotalHungry;
            public int TotalHomeless;
            public int TotalSick;
            public int TotalHoused;
            public int TotalHousingFree;
            public int TotalWarriorSlots;
            public float AvgHappiness;
            public int LowestLoyalty;
            public string LowestLoyaltyCity;

            // Building counts across kingdom
            public int TotalBuildings;
            public int TotalHouses;
            public int TotalBarracks;
            public int TotalFarms;
            public int TotalStockpiles;
            public int TotalStorages;
            public int TotalWatchTowers;
            public int UnderConstruction;

            // Per-city detail: name -> gold
            public Dictionary<string, int> CityGold = new Dictionary<string, int>();
            // Per-city detail: name -> population
            public Dictionary<string, int> CityPop = new Dictionary<string, int>();
            // Per-city detail: name -> biome
            public Dictionary<string, string> CityBiome = new Dictionary<string, string>();
            // Per-city detail: name -> loyalty
            public Dictionary<string, int> CityLoyalty = new Dictionary<string, int>();
            // Per-city detail: name -> hungry
            public Dictionary<string, int> CityHungry = new Dictionary<string, int>();
            // Per-city detail: name -> homeless
            public Dictionary<string, int> CityHomeless = new Dictionary<string, int>();
            // Per-city detail: name -> housing free
            public Dictionary<string, int> CityHousingFree = new Dictionary<string, int>();
            // Per-city detail: name -> warrior slots
            public Dictionary<string, int> CityWarriorSlots = new Dictionary<string, int>();
            // Per-city detail: name -> warriors current
            public Dictionary<string, int> CityWarriors = new Dictionary<string, int>();
            // Per-city detail: name -> all resources
            public Dictionary<string, Dictionary<string, int>> CityResources = new Dictionary<string, Dictionary<string, int>>();
            // Per-city detail: name -> building counts
            public Dictionary<string, Dictionary<string, int>> CityBuildings = new Dictionary<string, Dictionary<string, int>>();

            // Diplomatic opinions toward other kingdoms
            public Dictionary<string, int> OpinionOfOthers = new Dictionary<string, int>();
            // Diplomatic relations toward other kingdoms
            public Dictionary<string, string> RelationToOthers = new Dictionary<string, string>();
        }

        // The live database: KingdomName -> Snapshot
        public static Dictionary<string, KingdomSnapshot> Kingdoms { get; private set; }
            = new Dictionary<string, KingdomSnapshot>();

        /// <summary>
        /// Called once per AI cycle. Reads ALL data directly from game objects.
        /// </summary>
        public static void Refresh()
        {
            if (World.world == null) return;

            Kingdoms.Clear();

            foreach (Kingdom k in World.world.kingdoms.list)
            {
                if (!k.isAlive() || !k.isCiv()) continue;

                var snap = new KingdomSnapshot();
                snap.Name = k.name;
                snap.RulerName = k.king != null ? k.king.getName() : "None";
                snap.Population = k.getPopulationPeople();
                snap.ArmySize = k.countTotalWarriors();
                snap.CityCount = k.countCities();
                snap.Culture = k.culture?.name ?? "None";
                snap.Religion = k.religion?.name ?? "None";

                // Ruler stats
                if (k.king != null)
                {
                    snap.KingIntelligence = k.king.intelligence;
                    snap.KingDiplomacy = k.king.diplomacy;
                    snap.KingWarfare = k.king.warfare;
                    snap.KingStewardship = k.king.stewardship;
                    snap.KingRenown = k.king.renown;
                    snap.KingLevel = k.king.level;
                    snap.KingHasPlot = k.king.hasPlot();
                    if (snap.KingHasPlot && k.king.plot != null && k.king.plot.getAsset() != null)
                        snap.KingPlotType = k.king.plot.getAsset().id;
                }

                // Active plots by kingdom members
                try
                {
                    foreach (Plot plot in World.world.plots)
                    {
                        if (plot == null || !plot.isActive()) continue;
                        if (plot._plot_author != null && plot._plot_author.kingdom == k)
                        {
                            string plotInfo = plot.getAsset()?.id ?? "unknown";
                            if (plot.target_kingdom != null)
                                plotInfo += $" vs {plot.target_kingdom.name}";
                            else if (plot.target_city != null)
                                plotInfo += $" vs {plot.target_city.name}";
                            snap.ActivePlots.Add(plotInfo);
                        }
                    }
                }
                catch { /* ignore plot enumeration errors */ }

                if (k.culture != null)
                {
                    foreach (var trait in k.culture.getTraits())
                        snap.CultureTraits.Add(trait.id);
                }

                if (k.capital != null)
                {
                    WorldTile capTile = k.capital.getTile();
                    if (capTile != null && capTile.getBiome() != null)
                        snap.CapitalBiome = capTile.getBiome().id;
                }

                // Aggregate from all cities
                int kingdomGold = 0;
                int kingdomFood = 0;
                int kingdomWood = 0;
                int kingdomIron = 0;
                int kingdomStone = 0;
                int kingdomHungry = 0;
                int kingdomHomeless = 0;
                int kingdomSick = 0;
                int kingdomHoused = 0;
                int kingdomHousingFree = 0;
                int kingdomWarriorSlots = 0;
                int kingdomWarriors = 0;
                float totalHappiness = 0f;
                int happinessCount = 0;
                int lowestLoyalty = int.MaxValue;
                string lowestLoyaltyCity = "None";

                // Kingdom-wide building counts
                snap.TotalBuildings = k.buildings.Count;
                foreach (Building b in k.buildings)
                {
                    if (b == null || b.asset == null) continue;
                    switch (b.asset.type)
                    {
                        case "type_house": snap.TotalHouses++; break;
                        case "type_barracks": snap.TotalBarracks++; break;
                        case "type_farm": snap.TotalFarms++; break;
                        case "type_stockpile": snap.TotalStockpiles++; break;
                        case "type_storage": snap.TotalStorages++; break;
                        case "type_watch_tower": snap.TotalWatchTowers++; break;
                    }
                    if (b.isUnderConstruction()) snap.UnderConstruction++;
                }

                foreach (City city in k.getCities())
                {
                    if (city == null || city.isRekt()) continue;

                    // Resources
                    var cityRes = new Dictionary<string, int>();
                    foreach (var resAsset in AssetManager.resources.list)
                    {
                        int amount = city.getResourcesAmount(resAsset.id);
                        if (amount <= 0) continue;
                        cityRes[resAsset.id] = amount;
                        switch (resAsset.id)
                        {
                            case "gold": kingdomGold += amount; break;
                            case "wheat":
                            case "bread":
                            case "meat":
                            case "fish":
                            case "berries":
                                if (resAsset.food) kingdomFood += amount; break;
                            case "wood": kingdomWood += amount; break;
                            case "iron": kingdomIron += amount; break;
                            case "stone": kingdomStone += amount; break;
                        }
                    }

                    // Basic city stats
                    int cityPop = city.getPopulationPeople();
                    int cityGold = city.getResourcesAmount("gold");
                    int loyalty = city.getCachedLoyalty();

                    snap.CityNames.Add(city.name);
                    snap.CityGold[city.name] = cityGold;
                    snap.CityPop[city.name] = cityPop;
                    snap.CityLoyalty[city.name] = loyalty;
                    snap.CityHungry[city.name] = city.status.hungry;
                    snap.CityHomeless[city.name] = city.status.homeless;
                    snap.CityHousingFree[city.name] = city.status.housing_free;
                    snap.CityWarriorSlots[city.name] = city.status.warrior_slots;
                    snap.CityWarriors[city.name] = city.status.warriors_current;
                    snap.CityResources[city.name] = cityRes;

                    // Building counts per city
                    var cityBuildings = new Dictionary<string, int>();
                    foreach (Building b in city.buildings)
                    {
                        if (b == null || b.asset == null) continue;
                        string btype = b.asset.type ?? "unknown";
                        if (!cityBuildings.ContainsKey(btype)) cityBuildings[btype] = 0;
                        cityBuildings[btype]++;
                    }
                    snap.CityBuildings[city.name] = cityBuildings;

                    WorldTile cityTile = city.getTile();
                    if (cityTile != null && cityTile.getBiome() != null)
                        snap.CityBiome[city.name] = cityTile.getBiome().id;

                    // Kingdom aggregates
                    kingdomHungry += city.status.hungry;
                    kingdomHomeless += city.status.homeless;
                    kingdomSick += city.status.sick;
                    kingdomHoused += city.status.housed;
                    kingdomHousingFree += city.status.housing_free;
                    kingdomWarriorSlots += city.status.warrior_slots;
                    kingdomWarriors += city.status.warriors_current;

                    if (loyalty < lowestLoyalty)
                    {
                        lowestLoyalty = loyalty;
                        lowestLoyaltyCity = city.name;
                    }
                }

                // Count happiness from kingdom units
                foreach (Actor a in k.getUnits())
                {
                    if (a == null || !a.isAlive()) continue;
                    totalHappiness += a.getHappiness();
                    happinessCount++;
                }

                snap.TotalGold = kingdomGold;
                snap.TotalFood = kingdomFood;
                snap.TotalWood = kingdomWood;
                snap.TotalIron = kingdomIron;
                snap.TotalStone = kingdomStone;
                snap.TotalHungry = kingdomHungry;
                snap.TotalHomeless = kingdomHomeless;
                snap.TotalSick = kingdomSick;
                snap.TotalHoused = kingdomHoused;
                snap.TotalHousingFree = kingdomHousingFree;
                snap.TotalWarriorSlots = kingdomWarriorSlots;
                snap.AvgHappiness = happinessCount > 0 ? totalHappiness / happinessCount : 0f;
                snap.LowestLoyalty = lowestLoyalty == int.MaxValue ? 0 : lowestLoyalty;
                snap.LowestLoyaltyCity = lowestLoyaltyCity;

                // Active wars
                var wars = World.world.wars.getWars(k);
                if (wars != null)
                    snap.ActiveWars = wars.Select(w => w.ToString()).ToList();

                // Alliances
                foreach (Kingdom other in World.world.kingdoms.list)
                {
                    if (other == k || !other.isAlive() || !other.isCiv()) continue;
                    try
                    {
                        if (k.hasAlliance() && other.hasAlliance() && k.getAlliance() == other.getAlliance())
                            snap.AlliedKingdoms.Add(other.name);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AIBox] RealTimeDB alliance check failed for {k.name} vs {other.name}: {e.Message}");
                    }
                }

                // Diplomatic opinions and relations
                foreach (Kingdom other in World.world.kingdoms.list)
                {
                    if (other == k || !other.isAlive() || !other.isCiv()) continue;
                    try
                    {
                        KingdomOpinion opinion = World.world.diplomacy.getOpinion(k, other);
                        if (opinion != null)
                        {
                            snap.OpinionOfOthers[other.name] = opinion.total;
                            string relStatus = opinion.total > 30 ? "friendly" : (opinion.total < -30 ? "hostile" : "neutral");
                            snap.RelationToOthers[other.name] = relStatus;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AIBox] RealTimeDB opinion check failed for {k.name} vs {other.name}: {e.Message}");
                    }
                }

                Kingdoms[k.name] = snap;
            }
        }

        /// <summary>
        /// Build the full, accurate context string for the AI prompt.
        /// Replaces the old IntelligenceWrapper string — contains only real values.
        /// </summary>
        public static string BuildContextString(Kingdom k, KingdomBrain brain)
        {
            if (!Kingdoms.TryGetValue(k.name, out var snap))
                return "No data available.";

            string wars = snap.ActiveWars.Count > 0 ? string.Join(", ", snap.ActiveWars) : "None";
            string allies = snap.AlliedKingdoms.Count > 0 ? string.Join(", ", snap.AlliedKingdoms) : "None";

            string cityDetail = "";
            foreach (var cityName in snap.CityNames)
            {
                int g = snap.CityGold.ContainsKey(cityName) ? snap.CityGold[cityName] : 0;
                int p = snap.CityPop.ContainsKey(cityName) ? snap.CityPop[cityName] : 0;
                int l = snap.CityLoyalty.ContainsKey(cityName) ? snap.CityLoyalty[cityName] : 0;
                int h = snap.CityHungry.ContainsKey(cityName) ? snap.CityHungry[cityName] : 0;
                int hm = snap.CityHomeless.ContainsKey(cityName) ? snap.CityHomeless[cityName] : 0;
                int hf = snap.CityHousingFree.ContainsKey(cityName) ? snap.CityHousingFree[cityName] : 0;
                int ws = snap.CityWarriorSlots.ContainsKey(cityName) ? snap.CityWarriorSlots[cityName] : 0;
                int wc = snap.CityWarriors.ContainsKey(cityName) ? snap.CityWarriors[cityName] : 0;
                string biome = snap.CityBiome.ContainsKey(cityName) ? snap.CityBiome[cityName] : "unknown";

                string alerts = "";
                if (h > 0) alerts += $" HUNGRY:{h}";
                if (hm > 0) alerts += $" HOMELESS:{hm}";
                if (l < 0) alerts += $" DISLOYAL:{l}";
                if (hf == 0 && p > 0) alerts += " NO_FREE_HOUSING";
                if (ws > 0 && wc < ws) alerts += $" UNDERMANNED:{wc}/{ws}";

                cityDetail += $"  - {cityName}: {p} pop, {g}g, biome:{biome}, loyalty:{l}{alerts}\n";

                // Per-city resources (abbreviated)
                if (snap.CityResources.ContainsKey(cityName) && snap.CityResources[cityName].Count > 0)
                {
                    var resStr = string.Join(", ", snap.CityResources[cityName]
                        .Where(x => x.Value > 0)
                        .OrderByDescending(x => x.Value)
                        .Take(5)
                        .Select(x => $"{x.Key}:{x.Value}"));
                    if (!string.IsNullOrEmpty(resStr))
                        cityDetail += $"    Resources: {resStr}\n";
                }
            }

            // Building summary
            string bldgSummary = $"Buildings: Houses={snap.TotalHouses}, Barracks={snap.TotalBarracks}, Farms={snap.TotalFarms}, " +
                $"Stockpiles={snap.TotalStockpiles}, Storages={snap.TotalStorages}, Towers={snap.TotalWatchTowers}, " +
                $"UnderConstruction={snap.UnderConstruction}";

            // Ruler stats
            string rulerStats = "";
            if (k.king != null)
            {
                rulerStats = $"\nRuler Stats: {snap.RulerName} | INT:{snap.KingIntelligence} DIP:{snap.KingDiplomacy} WAR:{snap.KingWarfare} STE:{snap.KingStewardship} | Renown:{snap.KingRenown} Level:{snap.KingLevel}\n";
                if (snap.KingHasPlot)
                    rulerStats += $"  Currently plotting: {snap.KingPlotType ?? "unknown"}\n";
                else
                    rulerStats += $"  Currently plotting: NONE (king is free to start a plot)\n";
            }

            // Active plots
            string plots = "";
            if (snap.ActivePlots.Count > 0)
            {
                plots = "Active Plots:\n";
                foreach (var p in snap.ActivePlots.Take(5))
                    plots += $"  - {p}\n";
            }

            // Kingdom-wide alerts
            string kingdomAlerts = "";
            if (snap.TotalHungry > 0) kingdomAlerts += $"[HUNGRY:{snap.TotalHungry}] ";
            if (snap.TotalHomeless > 0) kingdomAlerts += $"[HOMELESS:{snap.TotalHomeless}] ";
            if (snap.TotalSick > 0) kingdomAlerts += $"[SICK:{snap.TotalSick}] ";
            if (snap.AvgHappiness < -20) kingdomAlerts += $"[LOW_MORALE:{snap.AvgHappiness:F0}] ";
            if (snap.LowestLoyalty < 0) kingdomAlerts += $"[DISLOYAL:{snap.LowestLoyaltyCity}({snap.LowestLoyalty})] ";
            if (snap.TotalHousingFree == 0 && snap.Population > 0) kingdomAlerts += "[NO_FREE_HOUSING] ";
            if (snap.ArmySize < snap.TotalWarriorSlots / 2 && snap.TotalWarriorSlots > 0) kingdomAlerts += $"[UNDERMANNED_ARMY:{snap.ArmySize}/{snap.TotalWarriorSlots}] ";

            string mail = MailRegistry.BuildInboxString(k.name);

            string surveys = "";
            if (brain.PendingSurveys.Count > 0)
            {
                surveys = "\nSURVEYS:\n";
                foreach (var pair in brain.PendingSurveys)
                    surveys += $"  - {pair.Key}: {pair.Value.Description}\n";
            }

            string cultureTraits = snap.CultureTraits.Count > 0 ? string.Join(", ", snap.CultureTraits) : "None";
            string capitalBiome = !string.IsNullOrEmpty(snap.CapitalBiome) ? snap.CapitalBiome : "Unknown";

            string diplomacy = "";
            if (snap.OpinionOfOthers.Count > 0 || snap.RelationToOthers.Count > 0)
            {
                diplomacy = "\nDIPLOMACY:\n";
                foreach (var other in World.world.kingdoms.list)
                {
                    if (other == k || !other.isAlive() || !other.isCiv()) continue;
                    string opinionStr = snap.OpinionOfOthers.ContainsKey(other.name) ? snap.OpinionOfOthers[other.name].ToString() : "?";
                    string relationStr = snap.RelationToOthers.ContainsKey(other.name) ? snap.RelationToOthers[other.name] : "neutral";
                    diplomacy += $"  - {other.name}: opinion={opinionStr}, relation={relationStr}\n";
                }
            }

            string intel = "";
            if (brain.IntelLevel.Count > 0)
            {
                intel = "\nINTEL ON ENEMIES:\n";
                foreach (var pair in brain.IntelLevel)
                    intel += $"  - {pair.Key}: intel level {pair.Value}\n";
            }

            // Real trend data from EconomicMemory
            string trends = "";
            if (brain.MemoryBank != null)
            {
                trends = "\nTRENDS (from memory):\n";
                trends += $"  Population: {brain.MemoryBank.GetPopulationTrend()}\n";
                trends += $"  Military: {brain.MemoryBank.GetMilitaryTrend()}\n";
                foreach (var res in brain.MemoryBank.ResourceHistory.Keys.OrderBy(x => x))
                {
                    trends += $"  {res}: {brain.MemoryBank.GetResourceTrend(res)}\n";
                }
            }

            string policy = $"\nPolicy: Tax={brain.TaxRate}, Budget={brain.MilitaryBudget}, Focus={brain.Focus}, Stance={brain.Stance}";

            string actionHistory = "\nRECENT ACTION HISTORY (what you did and results):\n";
            actionHistory += brain.GetRecentHistorySummary();

            // City priorities
            string cityPriorities = "";
            if (brain.CityPriorities.Count > 0)
            {
                cityPriorities = "\nCITY PRIORITIES:\n";
                foreach (var cp in brain.CityPriorities)
                    cityPriorities += $"  - {cp.Key}: {cp.Value}\n";
            }

            // Active plan
            string planStatus = "";
            if (brain.CurrentPlan != null && brain.CurrentPlan.Status == PlanStatus.Active)
            {
                planStatus = $"\nACTIVE PLAN: {brain.CurrentPlan.Name} (turn {brain.CurrentPlan.TurnsElapsed}/{brain.CurrentPlan.TargetTurns})\n";
                planStatus += $"  Target: {brain.CurrentPlan.TargetKingdom} / {brain.CurrentPlan.TargetCity}\n";
                planStatus += $"  Next step: {brain.CurrentPlan.NextStep}\n";
                if (brain.CurrentPlan.StepsCompleted.Count > 0)
                    planStatus += $"  Completed: {string.Join(", ", brain.CurrentPlan.StepsCompleted)}\n";
            }
            else if (brain.CompletedPlans.Count > 0)
            {
                planStatus = $"\nPAST PLANS: {string.Join("; ", brain.CompletedPlans.TakeLast(3))}\n";
            }

            // Combat stance
            string combatStatus = $"\nCOMBAT: WarStance={brain.WarStance}";
            if (!string.IsNullOrEmpty(brain.SiegeTargetCity))
                combatStatus += $", Sieging={brain.SiegeTargetCity}";
            combatStatus += "\n";

            string mechanicsNote = "\nIMPORTANT: You cannot directly create resources or place buildings. Citizens handle production automatically. Your role is strategic oversight.\n";

            // Event tracker: what happened since last turn
            string eventLog = brain.EventTracker.BuildEventLogString();

            // World laws
            string worldLaws = WorldLawTracker.BuildLawString();

            return
                eventLog +
                $"=== REAL-TIME KINGDOM DATA (from game engine) ===\n" +
                $"Kingdom: {snap.Name}\n" +
                $"Ruler: {snap.RulerName}\n" +
                $"Population: {snap.Population} | Army: {snap.ArmySize}/{snap.TotalWarriorSlots} warriors | Cities: {snap.CityCount}\n" +
                $"Cities ({snap.CityCount}):\n{cityDetail}" +
                $"{bldgSummary}\n" +
                $"Treasury (total gold): {snap.TotalGold}g\n" +
                $"Food: {snap.TotalFood} | Wood: {snap.TotalWood} | Iron: {snap.TotalIron} | Stone: {snap.TotalStone}\n" +
                $"Culture: {snap.Culture} (Traits: {cultureTraits}) | Religion: {snap.Religion}\n" +
                $"Capital Biome: {capitalBiome}\n" +
                $"Avg Happiness: {snap.AvgHappiness:F0} | Hungry: {snap.TotalHungry} | Homeless: {snap.TotalHomeless} | Sick: {snap.TotalSick}\n" +
                $"Active Wars: {wars}\n" +
                $"Allies: {allies}\n" +
                $"Alerts: {(string.IsNullOrEmpty(kingdomAlerts) ? "None" : kingdomAlerts)}" +
                rulerStats + plots + surveys + mail + diplomacy + intel + trends + policy + cityPriorities + planStatus + combatStatus + actionHistory + mechanicsNote +
                worldLaws +
                $"=================================================";
        }
    }
}
