# /// script
# requires-python = ">=3.9"
# dependencies = ["tabulate"]
# ///
"""
AIBox Token Cost Calculator
Estimates per-turn token usage and API costs across providers.
"""

import math

# ============== PRICING (per 1M tokens, as of Jan 2025) ==============
# These are approximate API prices. Local/Ollama is $0 (hardware cost only).
PRICING = {
    # Model: (input $/1M, output $/1M, notes)
    "OpenAI GPT-4o":           (2.50,   10.00,  "GPT-4o - fast, good reasoning"),
    "OpenAI GPT-4o-mini":      (0.15,    0.60,  "GPT-4o-mini - cheap, fast"),
    "Claude 3.5 Sonnet":       (3.00,   15.00,  "Claude 3.5 Sonnet - excellent reasoning"),
    "Claude 3.5 Haiku":        (0.25,    1.25,  "Claude 3.5 Haiku - fast, cheap"),
    "DeepSeek V3":             (0.14,    0.28,  "DeepSeek V3 - very cheap, strong"),
    "DeepSeek R1":             (0.55,    2.19,  "DeepSeek R1 - reasoning model"),
    "Ollama (Local)":          (0.00,    0.00,  "Free (GPU/electricity cost only)"),
}

# ============== TOKEN ESTIMATION ==============
# Our code uses: tokens = len(text) / 4 (rough estimate)
# In reality, tiktoken/claude tokenizers are more efficient for English (~3.5-4 chars/token)
# For mixed English/Russian, we'll use 4.0 as conservative estimate
CHARS_PER_TOKEN = 4.0

# ============== STATIC PROMPT (System Prompt) ==============
# This is sent EVERY call. It doesn't change between turns for the same kingdom.
SYSTEM_PROMPT_TEMPLATE = """{personality_block}

=== CRITICAL ANTI-HALLUCINATION RULES ===
1. You are playing a SIMULATION GAME called WorldBox...
[8 rules, ~600 chars]

{mechanics_guide}

{language_modifier}
Lore History: ...
World Tension: ...
Controlled Luxuries: ...
Active Spies: ...

{trend_report}

Describe your thoughts and then your action.
=== EVENT TRACKING (Your Morning Inbox) ===
[full event tracking guide, ~500 chars]

=== DIPLOMATIC MAIL SYSTEM ===
[mail guide, ~400 chars]

=== INTRIGUE / PLOT SYSTEM ===
[plot guide, ~400 chars]

=== CITY-LEVEL MANAGEMENT ===
[city guide, ~400 chars]

=== ENHANCED ESPIONAGE ===
[espionage guide, ~300 chars]

=== MULTI-TURN PLANNING ===
[planning guide, ~350 chars]

=== COMBAT TACTICS ===
[combat guide, ~300 chars]

Available Actions: [long list of ~30 actions, ~800 chars]
Format: THOUGHT: ... | ACTION: ...

=== SMART TARGETING ===
[smart targeting explanation, ~400 chars]
"""

# Approximate character counts for the static sections
STATIC_SECTIONS = {
    "personality_block":       120,   # "You are the King of X. Your personality is..."
    "anti_hallucination_rules": 650,   # 8 rules
    "mechanics_guide":         800,   # GameMechanicsResearch.GetMechanicsGuide()
    "language_modifier":       120,   # Russian/English language instruction
    "lore_history":             80,   # Typically 1-2 lines
    "world_tension_line":       60,   # "World Tension: 15% (Tense)"
    "luxuries_line":           50,   # "Controlled Luxuries: silk, spice"
    "spies_missions_line":     45,   # "Active Spies: 1 | Active Missions: 0"
    "trend_report_header":    120,   # Trend report is typically short (2-3 lines)
    "event_tracking_guide":   500,
    "diplomatic_mail_guide":  400,
    "intrigue_plot_guide":    400,
    "city_management_guide":  400,
    "enhanced_espionage_guide": 300,
    "multi_turn_planning_guide": 350,
    "combat_tactics_guide":   300,
    "available_actions_list": 800,
    "format_instruction":      60,
    "smart_targeting_guide":  400,
}

STATIC_PROMPT_CHARS = sum(STATIC_SECTIONS.values())
STATIC_PROMPT_TOKENS = int(STATIC_PROMPT_CHARS / CHARS_PER_TOKEN)

# ============== DYNAMIC CONTEXT (per kingdom, per turn) ==============
# This is BuildContextString() output. It varies by kingdom size and world state.

def estimate_context_chars(num_cities=3, num_other_kingdoms=5, has_mail=False, has_intel=False,
                            num_trends=4, num_events=5, has_plan=False, num_surveys=0):
    """Estimate character count of the dynamic context string."""

    # Header block
    header = 50  # "=== REAL-TIME KINGDOM DATA ===\nKingdom: X\nRuler: Y\n"
    header += 60  # Population/Army/Cities line

    # Cities section (per city ~120 chars base + resources)
    cities = num_cities * 120  # "  - CityName: 50 pop, 100g, biome:forest, loyalty:0"
    cities += num_cities * 60   # Resources line (when present)

    # Building summary
    bldg = 120  # "Buildings: Houses=5, Barracks=2, Farms=3..."

    # Resources
    resources = 80  # "Treasury: 500g\nFood: 200 | Wood: 150 | Iron: 50 | Stone: 30\n"

    # Culture/Religion/Biome/Happiness
    culture = 100  # Culture, Religion, Biome, Happiness, Hungry, Homeless, Sick

    # Wars & Allies
    wars_allies = 60  # "Active Wars: None\nAllies: Elves\n"

    # Alerts (conditional, ~40 chars avg)
    alerts = 40

    # Ruler stats
    ruler = 80  # "Ruler Stats: KingName | INT:5 DIP:3 WAR:4 STE:6 | Renown:10 Level:3"
    ruler += 40  # Plot status line

    # Active plots (rare, ~30 chars each, assume 1)
    plots = 30

    # Surveys (rare)
    surveys = num_surveys * 30

    # Mail (conditional)
    mail = 150 if has_mail else 0  # Inbox string

    # Diplomacy (per other kingdom ~30 chars)
    diplomacy = 10 + num_other_kingdoms * 35  # Header + per-kingdom opinion line

    # Intel (conditional)
    intel = 10 + (2 if has_intel else 0) * 25  # Header + per-enemy intel line

    # Trends (per resource ~25 chars)
    trends = 10 + num_trends * 25

    # Policy
    policy = 50  # "Policy: Tax=Medium, Budget=Balanced, Focus=Economic, Stance=Balanced"

    # Action history (~100 chars, 3-4 entries)
    action_history = 100

    # City priorities (rare, ~30 chars per city)
    city_priorities = num_cities * 25

    # Plan status (rare)
    plan_status = 80 if has_plan else 0

    # Combat status
    combat = 30

    # Mechanics note
    mechanics = 100

    # Event log (biggest variable - morning inbox)
    # [YOU], [GAME], [FROM] events - each ~60-100 chars
    event_log = 20 + num_events * 80  # Header + per-event

    # World laws
    world_laws = 400  # 14 laws listed

    # Footer
    footer = 30  # "=================================="

    total = (header + cities + bldg + resources + culture + wars_allies + alerts +
             ruler + plots + surveys + mail + diplomacy + intel + trends + policy +
             action_history + city_priorities + plan_status + combat + mechanics +
             event_log + world_laws + footer)

    return total


# ============== CALCULATIONS ==============

def calculate_cost(input_tokens, output_tokens, provider_name):
    """Calculate cost in USD for a single call."""
    input_price, output_price, _ = PRICING[provider_name]
    input_cost = (input_tokens / 1_000_000) * input_price
    output_cost = (output_tokens / 1_000_000) * output_price
    return input_cost + output_cost


def main():
    print("=" * 80)
    print("  AIBOX TOKEN & COST CALCULATOR")
    print("  Estimates per-turn API costs across providers")
    print("=" * 80)
    print()

    # --- Static prompt analysis ---
    print("[1] STATIC SYSTEM PROMPT (sent every call, identical per kingdom)")
    print("-" * 60)
    for section, chars in STATIC_SECTIONS.items():
        print(f"  {section:.<40} {chars:>5} chars = {int(chars/CHARS_PER_TOKEN):>4} tokens")
    print(f"  {'TOTAL STATIC':.<40} {STATIC_PROMPT_CHARS:>5} chars = {STATIC_PROMPT_TOKENS:>4} tokens")
    print()

    # --- Dynamic context analysis ---
    print("[2] DYNAMIC CONTEXT (BuildContextString - varies by game state)")
    print("-" * 60)

    scenarios = [
        ("Tiny Kingdom (1 city, 3 enemies)",   1, 3, False, False, 2, 2, False, 0),
        ("Small Kingdom (2 cities, 4 enemies)", 2, 4, False, False, 3, 4, False, 0),
        ("Medium Kingdom (3 cities, 5 enemies)", 3, 5, True,  True,  4, 6, True,  1),
        ("Large Kingdom (5 cities, 7 enemies)",  5, 7, True,  True,  5, 8, True,  2),
        ("Empire (8 cities, 10 enemies)",        8, 10, True, True,  6, 10, True, 3),
    ]

    print(f"  {'Scenario':<36} {'Chars':>7} {'Tokens':>8} {'+System':>8} {'Total In':>9}")
    print("  " + "-" * 78)

    context_results = {}
    for name, cities, enemies, mail, intel, trends, events, plan, surveys in scenarios:
        ctx_chars = estimate_context_chars(cities, enemies, mail, intel, trends, events, plan, surveys)
        ctx_tokens = int(ctx_chars / CHARS_PER_TOKEN)
        total_input = STATIC_PROMPT_TOKENS + ctx_tokens
        print(f"  {name:<36} {ctx_chars:>7} {ctx_tokens:>8} {STATIC_PROMPT_TOKENS:>8} {total_input:>9}")
        context_results[name] = (ctx_tokens, total_input)
    print()

    # Use Medium Kingdom as the "typical" scenario for cost calculations
    typical_ctx_tokens, typical_total_input = context_results["Medium Kingdom (3 cities, 5 enemies)"]

    # --- Per-kingdom, per-turn costs ---
    print("[3] PER-KINGDOM, PER-TURN COST (Medium Kingdom scenario)")
    print("-" * 60)
    print(f"  Input tokens:  {typical_total_input:>5} (system {STATIC_PROMPT_TOKENS} + context {typical_ctx_tokens})")
    print(f"  Output tokens: ~512 (MaxResponseTokens default)")
    print()
    print(f"  {'Provider':<25} {'Input $':>10} {'Output $':>10} {'Total $':>10}")
    print("  " + "-" * 58)
    for provider, (in_price, out_price, note) in PRICING.items():
        cost = calculate_cost(typical_total_input, 512, provider)
        in_cost = (typical_total_input / 1_000_000) * in_price
        out_cost = (512 / 1_000_000) * out_price
        print(f"  {provider:<25} {in_cost:>10.6f} {out_cost:>10.6f} {cost:>10.6f}")
    print()

    # --- Scale to full game world ---
    print("[4] FULL WORLD COSTS (per turn = one cycle through ALL kingdoms)")
    print("-" * 60)
    print("  Assumption: UpdateInterval = 5 seconds = 12 turns/minute")
    print("  Note: Rate limiting defaults to MinDelay=2s, MaxCalls/min=30")
    print()

    world_sizes = [
        ("Small World", 4),
        ("Medium World", 8),
        ("Large World", 12),
        ("Huge World", 20),
    ]

    print(f"  {'World Size':<14} {'Kingdoms':>8} {'Tokens/Turn':>12} {'$/Turn':>10} {'$/Min':>10} {'$/Hour':>10} {'$/Day':>10}")
    print("  " + "-" * 80)

    for world_name, num_kingdoms in world_sizes:
        # Adjust context for kingdom count (more kingdoms = more diplomacy lines)
        adj_ctx = estimate_context_chars(3, num_kingdoms - 1, True, True, 4, 6, True, 1)
        adj_total_input = STATIC_PROMPT_TOKENS + int(adj_ctx / CHARS_PER_TOKEN)
        total_tokens_per_turn = adj_total_input * num_kingdoms + (512 * num_kingdoms)  # All inputs + all outputs

        # Find cheapest and most expensive paid providers
        costs = {name: calculate_cost(adj_total_input, 512, name)
                 for name in PRICING if name != "Ollama (Local)"}

        cheapest = min(costs, key=costs.get)
        expensive = max(costs, key=costs.get)

        cheapest_cost = costs[cheapest]
        expensive_cost = costs[expensive]

        turns_per_minute = 12  # 5s interval
        # But rate limits cap at 30 calls/minute, so for 12+ kingdoms, not all fire every turn
        # Actually with 8 kingdoms @ 5s = 96 calls/minute, way over the 30/min limit
        # With rate limiting, effective turns are throttled
        effective_calls_per_minute = min(num_kingdoms * turns_per_minute, 30)  # Rate limit cap
        effective_turns_per_minute = effective_calls_per_minute / num_kingdoms if num_kingdoms > 0 else 0

        # For fair cost comparison, use the actual number of kingdoms that will fire per minute
        # With 30 calls/min cap and N kingdoms, each kingdom fires ~30/N times per minute
        calls_per_kingdom_per_minute = 30.0 / num_kingdoms
        # But also minimum delay: if MinDelay=2s, max is 30 calls/min per kingdom anyway
        # So 30/N is the binding constraint for N > 1

        cost_per_minute_cheapest = cheapest_cost * calls_per_kingdom_per_minute * num_kingdoms  # = cheapest_cost * 30
        cost_per_minute_expensive = expensive_cost * calls_per_kingdom_per_minute * num_kingdoms

        # Actually simpler: with 30 calls/min cap, total cost/min = 30 * per_call_cost
        cost_per_min_cheap = 30 * cheapest_cost
        cost_per_min_exp = 30 * expensive_cost

        # Per hour (assume continuous play)
        cost_per_hour_cheap = cost_per_min_cheap * 60
        cost_per_hour_exp = cost_per_min_exp * 60

        # Per day (8 hours play)
        cost_per_day_cheap = cost_per_hour_cheap * 8
        cost_per_day_exp = cost_per_hour_exp * 8

        print(f"  {world_name:<14} {num_kingdoms:>8} {total_tokens_per_turn:>12,} {cheapest_cost:>10.6f} {cost_per_min_cheap:>10.4f} {cost_per_hour_cheap:>10.2f} {cost_per_day_cheap:>10.2f}")
        print(f"  {'':>14} {'':>8} {'(cheapest: ' + cheapest + ')':>12} {'':>10} {'$/min':>10} {'$/hour':>10} {'$/8hr':>10}")
        print(f"  {'':>14} {'':>8} {'(most expensive: ' + expensive + ')':>12} {expensive_cost:>10.6f} {cost_per_min_exp:>10.4f} {cost_per_hour_exp:>10.2f} {cost_per_day_exp:>10.2f}")
        print()

    # --- Ollama (free) analysis ---
    print("[5] OLLAMA (LOCAL) - HARDWARE REQUIREMENTS")
    print("-" * 60)
    print("  Ollama is free to use but requires GPU/CPU resources.")
    print()
    print("  Model:           | VRAM Required | Context Window | Tokens/sec | Notes")
    print("  " + "-" * 75)
    ollama_models = [
        ("llama3 8B",     "~6 GB VRAM",  "128K",         "30-50",    "Fast, good quality"),
        ("llama3.1 8B",   "~6 GB VRAM",  "128K",         "35-55",    "Updated Llama 3"),
        ("mistral 7B",    "~5 GB VRAM",  "32K",          "40-60",    "Excellent reasoning"),
        ("mixtral 8x7B",  "~26 GB VRAM", "32K",          "15-25",    "MoE, very capable"),
        ("qwen2.5 7B",    "~5 GB VRAM",  "128K",         "40-60",    "Great multilingual"),
        ("gemma2 9B",     "~6 GB VRAM",  "8K",           "35-50",    "Google model"),
        ("deepseek-r1 7B","~5 GB VRAM",  "128K",         "20-30",    "Reasoning model"),
        ("phi4 14B",      "~10 GB VRAM", "16K",          "25-40",    "Microsoft, strong"),
    ]
    for model, vram, ctx, tps, note in ollama_models:
        print(f"  {model:<18} {vram:<14} {ctx:<14} {tps:<10} {note}")
    print()

    # --- Electricity cost estimate for Ollama ---
    print("  ELECTRICITY COST (Ollama local inference)")
    print("  " + "-" * 55)
    print(f"  GPU power draw: ~200-300W during inference")
    print(f"  Electricity cost: ~$0.15/kWh (US average)")
    print(f"  Cost per hour: ~${0.25 * 0.15:.4f} - ${0.30 * 0.15:.4f} (very rough estimate)")
    print()

    # --- DeepSeek context window advantage ---
    print("[6] CONTEXT WINDOW COMPARISON")
    print("-" * 60)
    print("  Current default: 4096 tokens")
    print("  Current typical input: ~{} tokens (system + context)".format(typical_total_input))
    print("  Remaining for response: {} tokens".format(4096 - typical_total_input - 100))
    print()
    print("  With 128K context window (Ollama/DeepSeek):")
    print("    Remaining for response: {} tokens".format(128000 - typical_total_input - 100))
    print("    Can send: ~{} turns of history without truncation".format((128000 - STATIC_PROMPT_TOKENS) // typical_ctx_tokens))
    print()
    print("  With 8192 context window (Claude Haiku / GPT-4o-mini):")
    print("    Remaining for response: {} tokens".format(8192 - typical_total_input - 100))
    print("    Can send: ~{} turns of history without truncation".format((8192 - STATIC_PROMPT_TOKENS) // typical_ctx_tokens))
    print()

    # --- Summary recommendation ---
    print("[7] RECOMMENDATIONS")
    print("-" * 60)
    print("  BUDGET TIER ($0.01-0.05/hour for 8 kingdoms):")
    print("    -> DeepSeek V3 or GPT-4o-mini")
    print("    -> Set ContextWindow=8192, MaxResponse=256")
    print("    -> Set MinDelay=5s, MaxCalls/min=12")
    print()
    print("  QUALITY TIER ($0.10-0.30/hour for 8 kingdoms):")
    print("    -> Claude 3.5 Sonnet or GPT-4o")
    print("    -> Set ContextWindow=16384, MaxResponse=512")
    print("    -> Set MinDelay=3s, MaxCalls/min=20")
    print()
    print("  UNLIMITED TIER ($0/hour):")
    print("    -> Ollama with llama3.1 8B or mistral 7B")
    print("    -> Requires 6-8GB VRAM GPU")
    print("    -> Set ContextWindow=32768 or 128000")
    print("    -> Can run with MinDelay=0.5s for snappy gameplay")
    print()
    print("  RUSSIAN LANGUAGE NOTE:")
    print("    Russian text is ~1.5x denser in tokens than English.")
    print("    Multiply all token estimates by ~1.3x for Russian.")
    print("    This increases costs proportionally.")
    print()

    print("=" * 80)


if __name__ == "__main__":
    main()
