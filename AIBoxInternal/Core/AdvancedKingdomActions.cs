using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// All actions here use REAL game mechanics. No resource injection, no cheating.
    /// The AI is a strategist, not a god.
    /// </summary>
    public static class AdvancedKingdomActions
    {
        // ============ MILITARY & DEFENSE ============

        public static void HireMercenaries(Kingdom k, KingdomBrain brain)
        {
            if (k.capital == null) return;

            // Check real gold
            int realGold = k.capital.getResourcesAmount("gold");
            if (realGold < 500)
            {
                brain.Memory.Add("MERCENARIES: Not enough gold to hire mercenaries.");
                return;
            }

            // Actually spend the gold from the capital city
            int cost = 500;
            k.capital.takeResource("gold", cost);
            brain.MemoryBank.LogSpending("HireMercenaries", "gold", cost, k.capital.name, "Bolster military with elite warriors");

            int count = 5;
            for (int i = 0; i < count; i++)
            {
                WorldTile tile = k.capital.getTile();
                if (tile == null) continue;

                Actor merc = World.world.units.spawnNewUnitByPlayer(k.asset.id, tile, true, false, 5f, null);
                if (merc != null)
                {
                    merc.joinCity(k.capital);
                    merc.addTrait("veteran");
                    merc.addTrait("strong");
                }
            }
            int goldNow = k.capital.getResourcesAmount("gold");
            brain.Memory.Add($"MERCENARIES: Hired {count} elite warriors for {cost}g. Capital gold now: {goldNow}g.");
            brain.EventTracker.RecordGameEvent("MILITARY", $"Hired {count} mercenaries for {cost}g. Capital gold: {goldNow}g.");
        }

        // ============ ESPIONAGE & COVERT ============

        public static void RecruitSpy(Kingdom k, Kingdom target, KingdomBrain brain, string missionType = "Infiltrate and Sabotage")
        {
            if (k.capital == null) return;

            int realGold = k.capital.getResourcesAmount("gold");
            if (realGold < 200)
            {
                brain.Memory.Add("ESPIONAGE: Not enough gold to recruit a spy.");
                return;
            }

            int cost = 200;
            k.capital.takeResource("gold", cost);
            brain.MemoryBank.LogSpending("RecruitSpy", "gold", cost, k.capital.name, $"Spy mission to {target.name}");

            WorldTile tile = k.capital.getTile();
            Actor spy = World.world.units.spawnNewUnitByPlayer(k.asset.id, tile, true, false, 5f, null);

            if (spy != null)
            {
                spy.getName();
                ((ActorData)spy.getData()).name = "Spy of " + k.name;
                spy.getData().favorite = true;
                spy.addTrait("agile");

                brain.ActiveMissions.Add(new ActiveMission {
                    Type = MissionType.Espionage,
                    TargetKingdom = target.name,
                    ActorID = spy.getData().id.ToString(),
                    Detail = missionType
                });
                brain.SpiesActive++;
                brain.IntelLevel[target.name] = brain.IntelLevel.ContainsKey(target.name) ? brain.IntelLevel[target.name] + 1 : 1;
                brain.Memory.Add($"ESPIONAGE: Dispatched a spy to {target.name}. Mission: {missionType}. Cost: {cost}g.");
                brain.EventTracker.RecordEspionage(target.name, $"Spy dispatched for {missionType}", brain.IntelLevel[target.name]);
            }
        }

        public static void Assassinate(Kingdom source, Kingdom target, Kingdom blameTarget = null)
        {
            var sBrain = MainController.Instance.Engine.GetBrains().ContainsKey(source) ? MainController.Instance.Engine.GetBrains()[source] : null;
            if (sBrain == null) return;

            // Check real gold from capital
            int realGold = source.capital != null ? source.capital.getResourcesAmount("gold") : 0;
            if (realGold < 1000)
            {
                sBrain.Memory.Add("ASSASSINATION: Not enough gold (need 1000).");
                return;
            }

            int cost = 1000;
            source.capital.takeResource("gold", cost);
            sBrain.MemoryBank.LogSpending("Assassinate", "gold", cost, source.capital.name, $"Target: {target.name}");

            Actor victim = target.king;
            if (victim == null || !victim.isAlive()) return;

            bool success = UnityEngine.Random.value < 0.6f;
            if (success)
            {
                BaseObjectData data = victim.getData();
                if (data != null) data.health = 1;
                victim.addTrait("cursed");
                sBrain.Memory.Add($"ASSASSINATION: Successfully eliminated King {victim.getName()} of {target.name}. Cost: {cost}g.");
            }
            else
            {
                sBrain.Memory.Add($"ASSASSINATION: Failed to eliminate King {victim.getName()} of {target.name}. Cost: {cost}g.");
            }

            if (blameTarget != null && blameTarget != source && UnityEngine.Random.value < 0.5f)
            {
                sBrain.Memory.Add($"DECEPTION: Our spies successfully blamed {blameTarget.name} for the assassination!");
            }
        }

        public static void Sabotage(Kingdom source, Kingdom target)
        {
            var sBrain = MainController.Instance.Engine.GetBrains().ContainsKey(source) ? MainController.Instance.Engine.GetBrains()[source] : null;
            if (sBrain == null) return;

            int realGold = source.capital != null ? source.capital.getResourcesAmount("gold") : 0;
            if (realGold < 300)
            {
                sBrain.Memory.Add("SABOTAGE: Not enough gold (need 300).");
                return;
            }

            int cost = 300;
            source.capital.takeResource("gold", cost);
            sBrain.MemoryBank.LogSpending("Sabotage", "gold", cost, source.capital.name, $"Target: {target.name}");

            var targetCities = target.getCities().ToList();
            if (targetCities.Count > 0)
            {
                City city = targetCities[UnityEngine.Random.Range(0, targetCities.Count)];
                if (city.buildings.Count > 0)
                {
                    Building b = city.buildings.GetRandom<Building>();
                    if (b != null) ReflectionUtility.CallMethod(b, "startDestroyBuilding");
                    sBrain.Memory.Add($"SABOTAGE: Destroyed critical infrastructure in {city.name}. Cost: {cost}g.");
                }
            }
        }

        // ============ DOMESTIC & STABILITY ============

        public static void HoldFestival(Kingdom k, KingdomBrain brain)
        {
            if (k.capital == null) return;

            int realGold = k.capital.getResourcesAmount("gold");
            int cost = k.getPopulationPeople() * 2;
            if (cost < 100) cost = 100;

            if (realGold < cost)
            {
                brain.Memory.Add($"FESTIVAL: Not enough gold (have {realGold}, need {cost}).");
                return;
            }

            k.capital.takeResource("gold", cost);
            brain.MemoryBank.LogSpending("HoldFestival", "gold", cost, k.capital.name, "Boost morale");

            // Apply happiness boost to all kingdom citizens
            foreach (Actor a in k.getUnits())
            {
                if (a != null && a.isAlive())
                    a.changeHappiness("festival", 10);
            }

            brain.Memory.Add($"CULTURE: Held a grand festival! Spent {cost}g. The people are happy and loyal.");
            brain.EventTracker.RecordCityEvent(k.capital.name, "Festival", $"Grand festival held. Cost: {cost}g. Happiness boosted for all citizens.");
        }

        // ============ TRADE & COMMERCE ============

        public static void StartTradeCaravan(Kingdom k, Kingdom target, KingdomBrain brain)
        {
            if (k.capital == null || target.capital == null) return;

            int realGold = k.capital.getResourcesAmount("gold");
            if (realGold < 100)
            {
                brain.Memory.Add("TRADE: Not enough gold to fund a trade caravan.");
                return;
            }

            int cost = 100;
            k.capital.takeResource("gold", cost);
            brain.MemoryBank.LogSpending("TradeCaravan", "gold", cost, k.capital.name, $"Destination: {target.name}");

            WorldTile tile = k.capital.getTile();
            Actor merchant = World.world.units.spawnNewUnitByPlayer(k.asset.id, tile, true, false, 5f, null);

            if (merchant != null)
            {
                ((ActorData)merchant.getData()).name = "Merchant of " + k.name;
                merchant.getData().favorite = true;
                merchant.addTrait("peaceful");

                brain.ActiveMissions.Add(new ActiveMission {
                    Type = MissionType.Trade,
                    TargetKingdom = target.name,
                    TargetCity = target.capital.name,
                    ActorID = merchant.getData().id.ToString(),
                    Detail = "Carrying diplomatic goods"
                });
                brain.Memory.Add($"COMMERCE: A trade caravan has departed for {target.name}. Cost: {cost}g.");
                brain.EventTracker.RecordTrade(target.name, true, $"Caravan departed with diplomatic goods. Cost: {cost}g.");
            }
        }

        // ============ EXPANSION & SETTLEMENT ============

        public static void SurveyLand(Kingdom k, KingdomBrain brain)
        {
            if (k.capital == null) return;

            brain.PendingSurveys.Clear();
            char siteLetter = 'A';

            for (int i = 0; i < 3; i++)
            {
                WorldTile targetTile = FindSettlementTile(k);
                if (targetTile != null)
                {
                    string siteName = "Site " + siteLetter;
                    string desc = GetTileDescription(targetTile);

                    brain.PendingSurveys[siteName] = new SurveyPoint {
                        TileIndex = targetTile.x + (targetTile.y * MapBox.width),
                        Description = desc
                    };
                    siteLetter++;
                }
            }

            if (brain.PendingSurveys.Count > 0)
            {
                brain.Memory.Add($"SURVEY: Our scouts have identified {brain.PendingSurveys.Count} potential sites for expansion.");
            }
        }

        private static string GetTileDescription(WorldTile tile)
        {
            string biome = tile.getBiome()?.id ?? "Unknown Biome";
            string resources = "Sparse";

            if (tile.chunk != null)
            {
                foreach(var neighbor in tile.neighboursAll)
                {
                    if (neighbor.building != null)
                    {
                        var asset = ReflectionUtility.GetField(typeof(Building), neighbor.building, "asset") as BuildingAsset;
                        if (asset != null && asset.id.Contains("ore")) resources = "Rich Ores";
                    }
                    if (neighbor.Type.id.Contains("grass")) resources = "Fertile Soil";
                }
            }

            return $"Biome: {biome}, Resources: {resources}, Distance: Far";
        }

        public static void PlanSettlement(Kingdom k, KingdomBrain brain, string siteName = null)
        {
            if (k.capital == null) return;

            // Find a valid settler from the kingdom
            Actor settler = null;
            foreach (Actor a in k.getUnits())
            {
                if (a != null && a.isAlive() && a.canBuildNewCity())
                {
                    settler = a;
                    break;
                }
            }

            if (settler == null)
            {
                brain.Memory.Add("EXPANSION: No available settlers. Need a free citizen to found a new city.");
                return;
            }

            int tileIndex = -1;
            WorldTile targetTile = null;

            if (!string.IsNullOrEmpty(siteName) && brain.PendingSurveys.ContainsKey(siteName))
            {
                tileIndex = brain.PendingSurveys[siteName].TileIndex;
                brain.PendingSurveys.Remove(siteName);
            }
            else
            {
                targetTile = FindSettlementTile(k);
                if (targetTile == null)
                {
                    brain.Memory.Add("EXPANSION: No suitable settlement site found.");
                    return;
                }
                tileIndex = targetTile.x + (targetTile.y * MapBox.width);
            }

            if (tileIndex == -1) return;

            int width = MapBox.width;
            int x = tileIndex % width;
            int y = tileIndex / width;
            targetTile = World.world.GetTile(x, y);

            if (targetTile == null || targetTile.zone == null || targetTile.zone.hasCity())
            {
                brain.Memory.Add("EXPANSION: Target zone is no longer available.");
                return;
            }

            // Send the settler
            settler.goTo(targetTile);

            brain.ActiveMissions.Add(new ActiveMission {
                Type = MissionType.Settlement,
                TargetCity = $"New {k.name} Colony",
                ActorID = settler.getData().id.ToString(),
                Progress = (float)tileIndex
            });
            brain.Memory.Add($"EXPANSION: Settler {settler.getName()} is moving to establish a new colony at {x},{y}.");
            brain.EventTracker.RecordSettlement($"{x},{y}", true);
        }

        private static WorldTile FindSettlementTile(Kingdom k)
        {
            for (int i = 0; i < 50; i++)
            {
                int x = UnityEngine.Random.Range(0, MapBox.width);
                int y = UnityEngine.Random.Range(0, MapBox.height);
                WorldTile randomTile = World.world.GetTile(x, y);

                if (randomTile == null || randomTile.Type.liquid) continue;
                if (randomTile.zone != null && randomTile.zone.hasCity()) continue;

                float dist = Vector2.Distance(new Vector2(randomTile.x, randomTile.y), new Vector2(k.capital.getTile().x, k.capital.getTile().y));
                if (dist > 20 && dist < 100) return randomTile;
            }
            return null;
        }

        // ============ STRATEGIC WARFARE ============

        public static void DeclareJustifiedWar(Kingdom attacker, Kingdom defender, string reason)
        {
            int myArmy = attacker.countTotalWarriors();
            int theirArmy = defender.countTotalWarriors();

            if (theirArmy > myArmy * 1.5f)
            {
                return; // Too dangerous
            }

            World.world.wars.newWar(attacker, defender, WarTypeLibrary.normal);
        }

        // ============ DIPLOMACY ============

        public static void FormAlliance(Kingdom k, Kingdom target, KingdomBrain brain)
        {
            if (target == null || k == target) return;
            if (k.hasAlliance() && k.getAlliance().kingdoms_hashset.Contains(target)) return;

            AllianceManager am = World.world.alliances;
            if (am != null)
            {
                am.forceAlliance(k, target);
                brain.Memory.Add($"DIPLOMACY: Formed an alliance with {target.name}.");
            }
        }

        public static void LeaveAlliance(Kingdom k, KingdomBrain brain)
        {
            if (!k.hasAlliance()) return;
            Alliance alliance = k.getAlliance();
            World.world.alliances.useDiscordPower(alliance, k.capital);
            brain.Memory.Add("DIPLOMACY: Left our alliance.");
        }

        public static void OfferPeace(Kingdom k, Kingdom target, KingdomBrain brain)
        {
            if (target == null) return;
            War war = World.world.wars.getWar(k, target, true);
            if (war != null)
            {
                World.world.wars.endWar(war, WarWinner.Peace);
                brain.Memory.Add($"DIPLOMACY: Offered peace to {target.name} and ended the war.");
            }
        }

        public static void BribeCity(Kingdom k, string targetCityName, KingdomBrain brain)
        {
            int realGold = k.capital != null ? k.capital.getResourcesAmount("gold") : 0;
            if (realGold < 1000)
            {
                brain.Memory.Add("BRIBE: Not enough gold (need 1000).");
                return;
            }

            City targetCity = World.world.cities.list.FirstOrDefault(c => c.name.Equals(targetCityName, StringComparison.OrdinalIgnoreCase));
            if (targetCity != null && targetCity.kingdom != k)
            {
                int cost = 1000;
                k.capital.takeResource("gold", cost);
                brain.MemoryBank.LogSpending("BribeCity", "gold", cost, k.capital.name, $"Target: {targetCity.name}");

                targetCity.joinAnotherKingdom(k, false, false);
                brain.Memory.Add($"COVERT: Bribed the city of {targetCity.name} to join our kingdom. Cost: {cost}g.");
            }
        }

        public static void AssassinateChief(Kingdom k, Kingdom target, KingdomBrain brain)
        {
            int realGold = k.capital != null ? k.capital.getResourcesAmount("gold") : 0;
            if (realGold < 500)
            {
                brain.Memory.Add("ASSASSINATION: Not enough gold (need 500).");
                return;
            }

            if (target == null || target.king == null || !target.king.isAlive()) return;

            int cost = 500;
            k.capital.takeResource("gold", cost);
            brain.MemoryBank.LogSpending("AssassinateChief", "gold", cost, k.capital.name, $"Target: {target.name}");

            Actor chief = target.king;
            if (chief != null && chief.isAlive())
            {
                if (UnityEngine.Random.value < 0.7f)
                {
                    chief.addTrait("cursed");
                    chief.addTrait("madness");
                    brain.Memory.Add($"ASSASSINATION: Cursed the ruler of {target.name} with madness. Cost: {cost}g.");
                }
                else
                {
                    brain.Memory.Add($"ASSASSINATION: Failed to curse the ruler of {target.name}. Cost: {cost}g.");
                }
            }
        }

        public static void PrayForMiracle(Kingdom k, KingdomBrain brain)
        {
            int realGold = k.capital != null ? k.capital.getResourcesAmount("gold") : 0;
            if (realGold < 2000)
            {
                brain.Memory.Add("MIRACLE: Not enough gold (need 2000).");
                return;
            }

            int cost = 2000;
            k.capital.takeResource("gold", cost);
            brain.MemoryBank.LogSpending("PrayForMiracle", "gold", cost, k.capital.name, "Divine intervention");

            World.world.diplomacy.eventFriendship(k);

            if (k.king != null) {
                k.king.addTrait("blessed");
                k.king.addTrait("shield");
            }
            brain.Memory.Add($"MIRACLE: Prayed to the Gods and received divine friendship and blessings! Cost: {cost}g.");
        }
    }
}
