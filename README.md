# AIBox Internal — Grand Strategy AI Engine for WorldBox

AIBox Internal is a powerful AI-driven mod for [WorldBox](https://store.steampowered.com/app/1206560/WorldBox__God_Simulator/) that turns every kingdom into an intelligent agent capable of diplomacy, espionage, economic planning, multi-turn strategy, and real-time chat.

Each kingdom is controlled by an external Large Language Model (LLM) — Ollama (local), OpenAI GPT, or Anthropic Claude — making every game unique, emergent, and unpredictable.

---

## Features

### Core AI Systems
- **Autonomous Kingdom AI** — Every kingdom makes its own decisions every turn based on real game data
- **Diplomacy Engine** — Declare war, sue for peace, send trade caravans, form alliances, and exchange mail with other kingdoms
- **Economic Memory** — Tracks trends in population, military, gold, food, and resources over time
- **Intelligence Network** — Spy on enemies, build intel levels, and receive espionage reports
- **Event Tracker** — AI receives a "morning inbox" of everything that happened since its last turn (wars, deaths, city captures, etc.)

### City-Level Management
- **City Priorities** — Assign strategic focus per city (Growth, Military, Economy, Loyalty, Food)
- **Conscription** — Force specific cities to train warriors
- **Resource Monitoring** — Per-city gold, population, loyalty, hunger, and housing tracking

### Enhanced Espionage
- **Steal Gold** — Extract enemy treasury intel
- **Sabotage Buildings** — Damage enemy infrastructure
- **Spy Missions** — Assign and track active operations with progress bars

### Multi-Turn Planning
- **Strategic Plans** — Create named plans with targets and durations (e.g. "Conquer Orcland")
- **Auto-Progress** — Plans auto-update each turn and complete when objectives are met
- **Plan History** — Review completed and expired plans

### Combat Tactics
- **War Stances** — Aggressive, Balanced, or Defensive posture
- **Siege City** — Order all warriors to assault a target city
- **Sally** — Charge from the capital
- **Retreat** — Recall all warriors to defend the capital

### Interview / Live Chat
- **Talk to any King** — Select a kingdom and have a real-time conversation with its AI ruler
- **Context-Aware** — The king knows its own stats, recent events, wars, and personality
- **Quick Actions** — One-click prompts for State Report, War Strategy, Economy, Diplomacy, Trade Deal, Plot Idea, City Needs, and Personality
- **Kingdom Selector** — Rich list with race icons, population, army size, and ruler names

### Native Event Hooks
The mod hooks real WorldBox events via Harmony patches:
- Actor deaths (with profession and cause)
- War declarations and peace treaties
- New city foundations
- City captures and conquests

These events feed directly into the AI's event log, so kings react to actual gameplay in real time.

### Save / Load Persistence
- All AI brains (memory, plans, history, chat logs) are saved automatically when the world is saved
- Restored automatically when loading a world — no progress lost

---

## Installation

### Prerequisites
1. **WorldBox v0.51.2+** (Steam)
2. **NeoModLoader** — Install via the in-game mod browser or manually

### Manual Install
1. Download the latest release from the [Releases](../../releases) page
2. Extract the `AIBoxInternal` folder into your WorldBox mods directory:
   ```
   steamapps/common/worldbox/worldbox_Data/StreamingAssets/mods/
   ```
   Final path should look like:
   ```
   worldbox_Data/StreamingAssets/mods/AIBoxInternal/AIBoxInternal.dll
   worldbox_Data/StreamingAssets/mods/AIBoxInternal/mod.json
   ```
3. Launch WorldBox and enable the mod in NeoModLoader

### From Source (Build Yourself)
1. Clone this repo
2. Open `AIBoxInternal/AIBoxInternal.csproj` in your IDE or build via CLI:
   ```bash
   dotnet build AIBoxInternal/AIBoxInternal.csproj
   ```
3. Copy the built DLL from `AIBoxInternal/bin/Debug/netstandard2.1/AIBoxInternal.dll` into your mods folder

---

## Configuration

### In-Game UI
Press the **AIBox Internal** window button or open the dashboard from the mod UI.

### Tabs
- **Logs** — Live feed of every kingdom's thoughts, actions, and events
- **Economy** — Resource sparklines and spending summaries per kingdom
- **Data Hub** — Sortable kingdom matrix with detailed city cards and divine whisper override
- **Military** — Active wars, attacker/defender breakdowns, army counts
- **Interview** — Live chat with any AI king
- **Map** — Strategic heatmap (wealth or tension) overlaid on the game world
- **Diplomacy** — Relationship web graph with war/alliance lines
- **AI Setup** — Configure global AI or per-kingdom providers
- **Settings** — Theme, language (English / Russian), opacity, font size, news ticker

### AI Provider Setup
1. Open **AI Setup** tab
2. Choose **Global AI** (one config for all kingdoms) or **Per-Kingdom AI** (individual configs)
3. Select provider:
   - **Ollama** (local, free) — Set endpoint to `http://localhost:11434` and model name (e.g. `llama3`, `mistral`, `qwen2.5`)
   - **OpenAI** — Enter your API key and model (e.g. `gpt-4o-mini`, `gpt-4o`)
   - **Claude** — Enter your API key and model (e.g. `claude-sonnet-4-20250514`)
4. Hit **Apply**

> **Tip:** Ollama is the cheapest option (free). For a balance of cost and quality, try `gpt-4o-mini` or Claude Haiku.

---

## Supported Languages

- **English** (default)
- **Russian** — Full UI localization, AI prompts, and chat responses

Change language in the **Settings** tab.

---

## Cost Estimates (2026)

Running this mod with cloud LLMs costs roughly:

| Provider | Model | Cost for 8 Kingdoms @ 30 calls/min |
|----------|-------|-----------------------------------|
| **Ollama** | Any local model | **$0** (runs on your GPU) |
| **DeepSeek** | V4 Flash (discount until May 31, 2026) | **~$0.83/hr** |
| **OpenAI** | GPT-4o-mini | **~$3.00/hr** |
| **OpenAI** | GPT-4o | **~$15.00/hr** |
| **Anthropic** | Claude Opus 4.7 | **~$43.56/hr** |

Rate limits are usually the bottleneck, not kingdom count. With Ollama on a modern GPU (RTX 3060+), you can run 8 kingdoms smoothly with sub-second response times.

---

## Architecture

```
AIBoxInternal/
├── Core/
│   ├── AIBoxEngine.cs          # Main loop: schedules AI cycles, executes actions
│   ├── AIProviderClient.cs       # HTTP clients for Ollama, OpenAI, Claude
│   ├── DeepSystems.cs            # Enums, plans, combat stances, city priorities
│   ├── EconomicMemory.cs         # Trend tracking and spending analysis
│   ├── IntelligenceWrapper.cs    # Prompt builder and response parser
│   ├── InterKingdomMail.cs       # Mail registry and diplomatic correspondence
│   ├── KingdomConfig.cs          # Per-kingdom AI provider settings
│   ├── KingdomEventTracker.cs    # Morning inbox and state diff detection
│   ├── RealTimeDB.cs             # Single source of truth for all game data
│   └── WorldLawTracker.cs        # Reads active world laws
├── Hooks/
│   └── HookManager.cs            # Harmony patches for native events
├── UI/
│   └── ImGuiRenderer.cs          # In-game dashboard, chat, heatmaps
└── Loader.cs                      # NeoModLoader entry point
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| AI not responding | Check your provider endpoint/API key in AI Setup. Verify Ollama is running (`ollama list`). |
| High token costs | Reduce kingdom count, increase cycle interval, or switch to Ollama/local models. |
| Mod crashes on load | Ensure you are on WorldBox v0.51.2. Check that NeoModLoader is up to date. |
| Interview chat is empty | Select a kingdom first. The chat only appears after choosing a ruler. |
| Events not showing | Native event hooks require Harmony. Ensure no other mod conflicts with `Actor.die`, `WarManager.newWar`, etc. |
| Save files grow large | Chat histories are saved with brains. Use the **Clear** button in Interview to reset chat for a kingdom. |

---

## Credits

- **Author:** BlackW1the
- **Built with:** NeoModLoader, HarmonyLib, Unity IMGUI
- **Inspiration:** Crusader Kings, Stellaris, Dwarf Fortress

---

## License

This project is provided as-is for educational and personal use. WorldBox is a trademark of Maxim Karpenko. Do not redistribute decompiled game assets.
