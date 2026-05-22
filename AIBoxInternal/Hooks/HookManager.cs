using HarmonyLib;
using System;
using UnityEngine;

namespace AIBoxInternal.Hooks
{
    public static class HookManager
    {
        private static Harmony _harmony;

        public static void Install()
        {
            try
            {
                _harmony = new Harmony("com.aibox.internal");
                _harmony.PatchAll();
                Debug.Log("[AIBox-Internal] Harmony Patches Installed.");
            }
            catch (Exception e)
            {
                Debug.LogError("[AIBox-Internal] Harmony Patching Failed: " + e.Message);
            }
        }

        public static void Uninstall()
        {
            _harmony?.UnpatchSelf();
        }
    }

    // Example Hook: Diplomacy Opinion
    [HarmonyPatch(typeof(DiplomacyManager), "getOpinion")]
    public static class DiplomacyPatch
    {
        public static void Postfix(ref KingdomOpinion __result, Kingdom k1, Kingdom k2)
        {
            if (k1 == null || k2 == null || __result == null) return;
            if (MainController.Instance == null || MainController.Instance.Engine == null) return;

            // Inject AI influence into opinion
            var brains = MainController.Instance.Engine.GetBrains();
            if (brains != null && brains.ContainsKey(k1))
            {
                var brain = brains[k1];
                if (brain != null && brain.IntelLevel != null && brain.IntelLevel.ContainsKey(k2.name))
                {
                    // In v0.51.2 we modify total directly
                    __result.total += 20;
                }
            }
        }
    }

    // Hook: City Resource Production (Macroeconomics)
    // We hook the AI behavior directly
    // NOTE: Old production patch that referenced fake EconomySim removed.
    // The AI no longer manipulates production directly. Citizens produce resources naturally.

    // Hook: World Laws (AI Master Control)
    [HarmonyPatch(typeof(WorldLaws), "isEnabled")]
    public static class WorldLawsPatch
    {
        public static void Postfix(ref bool __result, string pId)
        {
            // AI can "force" certain laws to be true/false globally
            if (MainController.Instance.Engine.ForceGlobalPeace && pId == "world_law_peace") {
                __result = true;
            }
        }
    }

    // Hook: Save Brains before world save
    [HarmonyPatch(typeof(SaveManager), "saveWorldToDirectory", new[] { typeof(string), typeof(bool), typeof(bool) })]
    public static class SaveBrainsPatch
    {
        public static void Prefix(string pFolder, bool pCompress = true, bool pCheckFolder = true)
        {
            if (MainController.Instance?.Engine != null)
            {
                MainController.Instance.Engine.SaveAllBrains();
            }
        }
    }

    // Hook: Load Brains after world load
    [HarmonyPatch(typeof(MapBox), "addLoadWorldCallbacks")]
    public static class LoadBrainsPatch
    {
        public static void Postfix()
        {
            // Use a delayed call because world may not be fully initialized yet
            if (MainController.Instance?.Engine != null)
            {
                // Delay to ensure kingdoms are fully loaded
                UnityEngine.Object.FindObjectOfType<MainController>()?.StartCoroutine(DelayedLoad());
            }
        }

        private static System.Collections.IEnumerator DelayedLoad()
        {
            yield return new WaitForSeconds(1.0f);
            if (MainController.Instance?.Engine != null)
            {
                MainController.Instance.Engine.LoadAllBrains();
            }
        }
    }

    // ============================================================
    // NATIVE EVENT HOOKS — Feed game events into KingdomEventTracker
    // ============================================================

    // --- Actor Death ---
    [HarmonyPatch(typeof(Actor), "die")]
    public static class ActorDiePatch
    {
        public static void Postfix(Actor __instance)
        {
            if (__instance?.kingdom == null || !__instance.kingdom.isCiv()) return;
            var engine = MainController.Instance?.Engine;
            if (engine == null) return;
            var brain = engine.GetBrainForKingdom(__instance.kingdom);
            if (brain != null && brain.EventTracker != null)
            {
                string actorName = __instance.getName() ?? "A citizen";
                string profession = __instance.getProfession().ToString().ToLowerInvariant();
                string cause = __instance.getHealth() <= 0 ? "in battle" : "of natural causes";
                brain.EventTracker.RecordGameEvent("CITIZEN_DEATH",
                    $"{actorName} ({profession}) died {cause}.");
            }
        }
    }

    // --- War Start ---
    [HarmonyPatch(typeof(WarManager), "newWar")]
    public static class WarStartPatch
    {
        public static void Postfix(War __result, Kingdom pAttacker, Kingdom pDefender)
        {
            if (__result == null || pAttacker == null || pDefender == null) return;
            var engine = MainController.Instance?.Engine;
            if (engine == null) return;

            // Notify attacker
            if (pAttacker.isCiv())
            {
                var brain = engine.GetBrainForKingdom(pAttacker);
                brain?.EventTracker?.RecordGameEvent("WAR",
                    $"You declared war on {pDefender.name}! (War: {__result.name})");
            }
            // Notify defender
            if (pDefender.isCiv())
            {
                var brain = engine.GetBrainForKingdom(pDefender);
                brain?.EventTracker?.RecordIncomingWar(pAttacker.name, $"War: {__result.name}");
            }
        }
    }

    // --- War End ---
    [HarmonyPatch(typeof(WarManager), "endWar")]
    public static class WarEndPatch
    {
        public static void Prefix(War pWar)
        {
            if (pWar == null || !pWar.isAlive()) return;
            var engine = MainController.Instance?.Engine;
            if (engine == null) return;

            var attackers = pWar.getAttackers();
            var defenders = pWar.getDefenders();
            string warName = pWar.name ?? "A war";

            foreach (var k in attackers)
            {
                if (k == null || !k.isCiv()) continue;
                var brain = engine.GetBrainForKingdom(k);
                brain?.EventTracker?.RecordGameEvent("PEACE",
                    $"War '{warName}' has ended.");
            }
            foreach (var k in defenders)
            {
                if (k == null || !k.isCiv()) continue;
                var brain = engine.GetBrainForKingdom(k);
                brain?.EventTracker?.RecordGameEvent("PEACE",
                    $"War '{warName}' has ended.");
            }
        }
    }

    // --- New City Founded ---
    [HarmonyPatch(typeof(CityManager), "buildNewCity")]
    public static class CityFoundedPatch
    {
        public static void Postfix(City __result, Actor pActor)
        {
            if (__result == null || pActor?.kingdom == null || !pActor.kingdom.isCiv()) return;
            var engine = MainController.Instance?.Engine;
            if (engine == null) return;
            var brain = engine.GetBrainForKingdom(pActor.kingdom);
            if (brain != null && brain.EventTracker != null)
            {
                brain.EventTracker.RecordGameEvent("CITY_GAINED",
                    $"New city founded: '{__result.name}' by {pActor.getName()}.");
            }
        }
    }

    // --- City Captured / Kingdom Changed ---
    [HarmonyPatch(typeof(City), "setKingdom")]
    public static class CityCapturedPatch
    {
        public static void Postfix(City __instance, Kingdom pKingdom, bool pFromLoad)
        {
            if (pFromLoad || pKingdom == null || __instance == null) return;
            var engine = MainController.Instance?.Engine;
            if (engine == null) return;

            // Notify new owner
            if (pKingdom.isCiv())
            {
                var brain = engine.GetBrainForKingdom(pKingdom);
                if (brain != null && brain.EventTracker != null)
                {
                    brain.EventTracker.RecordGameEvent("CITY_GAINED",
                        $"We captured the city '{__instance.name}'!");
                }
            }
        }
    }
}
