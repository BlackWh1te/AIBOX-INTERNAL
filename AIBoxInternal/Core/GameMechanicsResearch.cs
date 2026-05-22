using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// Static knowledge base about how WorldBox v0.51.2 actually works.
    /// This is fed to the AI so it understands real game mechanics instead of hallucinating.
    /// All data comes from reverse-engineering Assembly-CSharp.
    /// </summary>
    public static class GameMechanicsResearch
    {
        public static string GetMechanicsGuide()
        {
            return
                "=== HOW THE GAME ACTUALLY WORKS (Verified from v0.51.2 code) ===\n" +
                "\n" +
                "--- RESOURCES ---\n" +
                "Resources are stored in BUILDINGS (stockpiles and storages), NOT in the city directly.\n" +
                "Each city sums resources across all its usable storage buildings.\n" +
                "Resources have a MAXIMUM per-storage (ResourceAsset.maximum, usually 999).\n" +
                "Resources have a STACK_SIZE (usually 15) for inventory purposes.\n" +
                "Resources have a PRODUCE_MIN (default 10) - if below this, production spikes.\n" +
                "Resources have INGREDIENTS - producing advanced resources requires consuming basic ones.\n" +
                "Example: To produce 'bread', a city needs 'wheat' as an ingredient.\n" +
                "Production happens automatically via CityBehProduceResources every tick.\n" +
                "Amount produced = (city population / 10) + 1 per resource tick.\n" +
                "The AI (you) CANNOT directly produce resources. Citizens do this automatically.\n" +
                "\n" +
                "--- BUILDINGS ---\n" +
                "Buildings are constructed by the city's internal AI (CityBehBuild) every 5 seconds.\n" +
                "Each city follows a BUILD ORDER TEMPLATE based on its race (ActorAsset.build_order_template_id).\n" +
                "Buildings cost resources (BuildingAsset.cost) which are consumed from city storages.\n" +
                "Buildings have types: type_house, type_barracks, type_storage, type_stockpile, type_farm, etc.\n" +
                "Houses provide HOUSING SLOTS. Without enough houses, population cannot grow.\n" +
                "Maximum houses = zones count + bonus from buildings + culture traits.\n" +
                "Culture traits that affect housing: dense_dwellings (x2), solitude_seekers (/3), hive_society (x3).\n" +
                "The AI (you) CANNOT directly place buildings. You influence what gets built indirectly via culture.\n" +
                "\n" +
                "--- CITY EXPANSION ---\n" +
                "New cities are founded by ACTORS (settlers), not by kingdom decree.\n" +
                "An actor can build a new city if: their current zone has NO city, they have NO city, and the zone is suitable.\n" +
                "The actor must be sent to an empty TileZone. World.world.cities.buildNewCity(actor, zone) creates it.\n" +
                "New cities start with the founding actor's species and join the actor's kingdom.\n" +
                "Cities auto-expand their territory by claiming neighboring empty zones over time.\n" +
                "\n" +
                "--- INTER-CITY TRADE (This IS possible!) ---\n" +
                "The game already has a supply system: CityBehSupplyKingdomCities.\n" +
                "When a city has a resource above 'supply_bound_give' (default 30), it can send to another city.\n" +
                "The receiving city must be below 'supply_bound_take' (default 10).\n" +
                "Transfer amount = 'supply_give' (default 10) per tick.\n" +
                "Supply cooldown = 30 seconds base, modified by city leader's 'multiplier_supply_timer' stat.\n" +
                "This happens automatically between cities in the SAME KINGDOM.\n" +
                "\n" +
                "--- HAPPINESS & LOYALTY ---\n" +
                "Each actor has happiness: -100 (suicidal) to +100 (ecstatic). Ratio = (happiness+100)/200.\n" +
                "Happy = ratio >= 0.6. Sad = ratio < 0.3.\n" +
                "Happiness events: just_born, just_lost_house, just_made_friend, just_pooped, just_had_child, was_conquered, etc.\n" +
                "Each CITY tracks: hungry, sick, homeless, housed, warrior slots, warrior current.\n" +
                "City loyalty is calculated by LoyaltyCalculator. Cached for 3 seconds.\n" +
                "Loyalty < 0 means the city is unhappy and may rebel.\n" +
                "Hunger = actors with getNutritionRatio() <= nutrition_level_hungry.\n" +
                "\n" +
                "--- ARMY & WAR ---\n" +
                "Warriors are citizens assigned the UnitProfession.Warrior job.\n" +
                "Max warriors = status.warrior_slots (from barracks/training buildings).\n" +
                "Army size multiplier = civ_base_army_multiplier + leader's 'army' stat + (leader's 'warfare' * 2 / 100).\n" +
                "Wars are declared via World.world.wars.newWar(attacker, defender, warType).\n" +
                "Cities can be captured by having units stand in them (addCapturePoints).\n" +
                "Watch towers add +10 capture defense per tower.\n" +
                "\n" +
                "--- WHAT THE AI CAN ACTUALLY DO ---\n" +
                "1. Declare war, offer peace, form alliances (diplomatic actions).\n" +
                "2. Send spies for intel or sabotage (espionage missions).\n" +
                "3. Settle new cities by sending actors to empty zones (if they can build).\n" +
                "4. Set kingdom policy: tax rate, military budget, national focus, military stance.\n" +
                "5. Send messages to other kingdoms (diplomatic mail).\n" +
                "6. Influence culture traits (some can be added via culture.addTrait()).\n" +
                "7. Hire mercenaries (spawns elite units at capital, costs real resources).\n" +
                "8. Pray for miracles (diplomacy.eventFriendship + bless king).\n" +
                "\n" +
                "--- WHAT THE AI CANNOT DO (no cheating) ---\n" +
                "1. Cannot directly add gold, wood, stone, or any resource to cities.\n" +
                "2. Cannot directly place or upgrade buildings.\n" +
                "3. Cannot force citizens to gather specific resources.\n" +
                "4. Cannot teleport units or resources.\n" +
                "5. Cannot create population out of thin air.\n" +
                "\n" +
                "=== END MECHANICS GUIDE ===";
        }

        /// <summary>
        /// Returns all resource types that exist in the game.
        /// </summary>
        public static List<string> GetAllResourceTypes()
        {
            var list = new List<string>();
            if (AssetManager.resources != null)
            {
                foreach (var res in AssetManager.resources.list)
                {
                    if (res != null && !string.IsNullOrEmpty(res.id))
                        list.Add(res.id);
                }
            }
            return list;
        }

        /// <summary>
        /// Get a human-readable description of a resource's production chain.
        /// </summary>
        public static string GetResourceChain(string resourceId)
        {
            var asset = AssetManager.resources.get(resourceId);
            if (asset == null) return $"{resourceId}: unknown resource.";

            string desc = $"{resourceId}: max={asset.maximum}, produce_min={asset.produce_min}";
            if (asset.food) desc += ", is_food";
            if (asset.wood) desc += ", is_wood";
            if (asset.mineral) desc += ", is_mineral";

            if (asset.ingredients != null && asset.ingredients.Length > 0)
            {
                desc += $", requires ingredients: {string.Join(", ", asset.ingredients)} x{asset.ingredients_amount}";
            }
            else
            {
                desc += ", no ingredients needed (base resource)";
            }

            desc += $", supply_give={asset.supply_give}, supply_bound_give={asset.supply_bound_give}, supply_bound_take={asset.supply_bound_take}";
            return desc;
        }

        /// <summary>
        /// Get all buildings currently in a city with their types.
        /// </summary>
        public static Dictionary<string, int> GetBuildingCounts(City city)
        {
            var counts = new Dictionary<string, int>();
            if (city == null || city.buildings == null) return counts;

            foreach (Building b in city.buildings)
            {
                if (b == null || b.asset == null) continue;
                string type = b.asset.type ?? "unknown";
                if (!counts.ContainsKey(type)) counts[type] = 0;
                counts[type]++;
            }
            return counts;
        }

        /// <summary>
        /// Get all building IDs in a city.
        /// </summary>
        public static Dictionary<string, int> GetBuildingIDCounts(City city)
        {
            var counts = new Dictionary<string, int>();
            if (city == null || city.buildings == null) return counts;

            foreach (Building b in city.buildings)
            {
                if (b == null || b.asset == null) continue;
                string id = b.asset.id ?? "unknown";
                if (!counts.ContainsKey(id)) counts[id] = 0;
                counts[id]++;
            }
            return counts;
        }

        /// <summary>
        /// Analyze what a city is missing for healthy growth.
        /// </summary>
        public static string GetCityHealthDiagnosis(City city)
        {
            if (city == null) return "City is null.";
            string diag = "";

            if (city.status.hungry > 0)
                diag += $"HUNGER CRISIS: {city.status.hungry} citizens are hungry. Need more food production or storage. ";
            if (city.status.homeless > 0)
                diag += $"HOUSING CRISIS: {city.status.homeless} citizens are homeless. Need more houses. Max houses={city.status.houses_max}, current={city.getHouseCurrent()}. ";
            if (city.status.sick > 0)
                diag += $"HEALTH CRISIS: {city.status.sick} citizens are sick. ";
            if (city.status.warriors_current < city.status.warrior_slots)
                diag += $"UNDERMANNED ARMY: {city.status.warriors_current}/{city.status.warrior_slots} warrior slots filled. ";
            if (city.getCachedLoyalty() < 0)
                diag += $"LOYALTY CRISIS: Loyalty is {city.getCachedLoyalty()}. City may rebel! ";
            if (city.status.housing_free == 0 && city.status.population >= city.status.housing_total)
                diag += $"POPULATION CAPPED: No free housing. Population cannot grow. ";

            if (string.IsNullOrEmpty(diag))
                diag = "City is healthy. No critical issues detected.";

            return diag;
        }

        /// <summary>
        /// Check if a kingdom has cities that could supply resources to struggling ones.
        /// </summary>
        public static string GetSupplyOpportunities(Kingdom k)
        {
            string result = "";
            var cities = k.getCities().ToList();
            if (cities.Count < 2) return "Need at least 2 cities for internal trade.";

            foreach (City donor in cities)
            {
                foreach (string res in GetAllResourceTypes())
                {
                    int amount = donor.getResourcesAmount(res);
                    var asset = AssetManager.resources.get(res);
                    if (asset == null) continue;

                    if (amount > asset.supply_bound_give)
                    {
                        foreach (City recipient in cities)
                        {
                            if (recipient == donor) continue;
                            int recipientAmount = recipient.getResourcesAmount(res);
                            if (recipientAmount < asset.supply_bound_take)
                            {
                                result += $"{donor.name} has excess {res} ({amount}), {recipient.name} is low ({recipientAmount}). Supply possible! ";
                            }
                        }
                    }
                }
            }
            return string.IsNullOrEmpty(result) ? "No supply imbalances detected." : result;
        }
    }
}
