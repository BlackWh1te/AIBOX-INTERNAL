using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    [Serializable]
    public class KingdomState
    {
        public string KingdomName;
        public string RulerName;
        public List<string> RulerTraits;
        public int TotalPopulation;
        public int ArmyCount;
        public Dictionary<string, int> Resources;
        public List<string> ActiveWars;
        public string Culture;
        public string Religion;
        public List<string> RecentHistory;
    }

    public static class IntelligenceWrapper
    {
        public static KingdomState GetState(Kingdom k)
        {
            KingdomState state = new KingdomState();
            state.KingdomName = k.name;
            
            if (k.king != null) {
                state.RulerName = k.king.getName();
                var data = k.king.getData();
                if (data != null) {
                     // Reflection to safely get traits
                     var field = data.GetType().GetField("traits");
                     if (field != null) state.RulerTraits = ((List<string>)field.GetValue(data)).ToList();
                     else state.RulerTraits = new List<string>();
                }
                else state.RulerTraits = new List<string>();
            }

            state.TotalPopulation = k.getPopulationPeople();
            state.ArmyCount = k.countTotalWarriors();
            
            // Collect Resources
            state.Resources = new Dictionary<string, int>();
            foreach(var city in k.getCities()) {
                if (city == null || city.isRekt()) continue;
                foreach(var res in AssetManager.resources.list) {
                    if (!state.Resources.ContainsKey(res.id)) state.Resources[res.id] = 0;
                    state.Resources[res.id] += city.getResourcesAmount(res.id);
                }
            }

            state.ActiveWars = World.world.wars.getWars(k).Select(w => w.ToString()).ToList();
            state.Culture = k.culture?.name ?? "None";
            state.Religion = k.religion?.name ?? "None";
            
            // Get recent history from Brain
            if (MainController.Instance.Engine.GetBrains().TryGetValue(k, out var brain)) {
                state.RecentHistory = brain.Memory.TakeLast(5).ToList();
            }

            return state;
        }

        public static string ToContextString(KingdomState state, KingdomBrain brain)
        {
            string resources = "";
            foreach (var kvp in state.Resources)
            {
                if (kvp.Value > 0) resources += $"{kvp.Key}: {kvp.Value}, ";
            }
            if (resources.Length > 2) resources = resources.Substring(0, resources.Length - 2);

            string wars = state.ActiveWars.Count > 0 ? string.Join(", ", state.ActiveWars) : "None";
            string traits = state.RulerTraits.Count > 0 ? string.Join(", ", state.RulerTraits) : "None";

            string cityStatus = "";
            foreach(var city in brain.CityData.Values) {
                if(city.IsDistressed) cityStatus += $"[{city.Name} is distressed] ";
            }

            string surveyInfo = "";
            if (brain.PendingSurveys.Count > 0)
            {
                surveyInfo = "Surveys: ";
                foreach(var pair in brain.PendingSurveys) {
                    surveyInfo += $"[{pair.Key}: {pair.Value.Description}] ";
                }
            }
            
            string mail = MailRegistry.BuildInboxString(state.KingdomName);

            return $"Kingdom: {state.KingdomName} | Ruler: {state.RulerName} (Traits: {traits}) | Pop: {state.TotalPopulation} | Army: {state.ArmyCount} | Cities: {brain.CityData.Count}\n" +
                   $"Resources: {resources}\n" +
                   $"Culture: {state.Culture} | Religion: {state.Religion} | Wars: {wars}\n" +
                   $"Status: {cityStatus}{surveyInfo}{mail}";
        }
    }
}
