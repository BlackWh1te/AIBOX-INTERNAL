using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// Tracks world law states and exposes them to the AI.
    /// Also handles AI advocacy for law changes.
    /// </summary>
    public static class WorldLawTracker
    {
        // Relevant laws for kingdom AI strategy
        public static readonly string[] StrategicLaws = new[]
        {
            "world_law_diplomacy",
            "world_law_rebellions",
            "world_law_border_stealing",
            "world_law_kingdom_expansion",
            "world_law_civ_army",
            "world_law_civ_limit_population_100",
            "world_law_old_age",
            "world_law_civ_babies",
            "world_law_civ_migrants",
            "world_law_peaceful_monsters",
            "world_law_angry_civilians",
            "world_law_hunger",
            "world_law_disasters_nature",
            "world_law_disasters_other"
        };

        private static readonly Dictionary<string, string> LawDescriptions = new Dictionary<string, string>
        {
            { "world_law_diplomacy", "Enables alliances, plots, and formal diplomacy between kingdoms" },
            { "world_law_rebellions", "Allows cities to rebel and form new kingdoms" },
            { "world_law_border_stealing", "Cities can steal territory from neighboring cities" },
            { "world_law_kingdom_expansion", "Cities can expand their territory naturally" },
            { "world_law_civ_army", "Civilians can become warriors when needed" },
            { "world_law_civ_limit_population_100", "Limits city population to 100 per city" },
            { "world_law_old_age", "Citizens die of old age" },
            { "world_law_civ_babies", "Citizens can have children and reproduce" },
            { "world_law_civ_migrants", "Citizens can migrate between cities" },
            { "world_law_peaceful_monsters", "Monsters do not attack cities automatically" },
            { "world_law_angry_civilians", "Civilians can become angry and riot" },
            { "world_law_hunger", "Citizens need food or they starve" },
            { "world_law_disasters_nature", "Natural disasters (tornadoes, earthquakes) can occur" },
            { "world_law_disasters_other", "Other disasters (meteors, etc.) can occur" }
        };

        /// <summary>
        /// Get the current state of all strategic world laws.
        /// </summary>
        public static Dictionary<string, bool> GetLawStates()
        {
            var result = new Dictionary<string, bool>();
            foreach (var lawId in StrategicLaws)
            {
                try
                {
                    var law = AssetManager.world_laws_library.get(lawId);
                    result[lawId] = law != null && law.isEnabled();
                }
                catch
                {
                    result[lawId] = false;
                }
            }
            return result;
        }

        /// <summary>
        /// Build a readable string of world law states for the AI prompt.
        /// </summary>
        public static string BuildLawString()
        {
            var states = GetLawStates();
            if (states.Count == 0) return "";

            string result = "\n=== WORLD LAWS (affect all kingdoms) ===\n";
            foreach (var kvp in states)
            {
                string shortName = kvp.Key.Replace("world_law_", "").Replace("_", " ");
                string status = kvp.Value ? "ON" : "OFF";
                string color = kvp.Value ? "#66ff66" : "#ff6666";
                string desc = "";
                LawDescriptions.TryGetValue(kvp.Key, out desc);
                result += $"  <color={color}>[{status}]</color> {shortName}{(string.IsNullOrEmpty(desc) ? "" : $" — {desc}")}\n";
            }
            return result;
        }

        /// <summary>
        /// Toggle a world law. Returns success status and a message.
        /// </summary>
        public static bool ToggleLaw(string lawId, bool enable)
        {
            try
            {
                var law = AssetManager.world_laws_library.get(lawId);
                if (law == null) return false;
                law.toggle(enable);
                Debug.Log($"[AIBox] World law '{lawId}' toggled to {(enable ? "enabled" : "disabled")}.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIBox] Failed to toggle law '{lawId}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if a law ID is valid and known.
        /// </summary>
        public static bool IsValidLaw(string lawId)
        {
            return AssetManager.world_laws_library.get(lawId) != null;
        }

        /// <summary>
        /// Get a short list of only the laws that are currently ON.
        /// </summary>
        public static List<string> GetEnabledLaws()
        {
            return GetLawStates()
                .Where(kvp => kvp.Value)
                .Select(kvp => kvp.Key.Replace("world_law_", ""))
                .ToList();
        }
    }
}
