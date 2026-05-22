# /// script
# requires-python = ">=3.9"
# dependencies = ["tabulate"]
# ///
"""
AIBox Token Cost Calculator — 2026 Edition
All pricing verified from official API docs as of May 2026.
"""

import math
from tabulate import tabulate

# ============== 2026 API PRICING (per 1M tokens) ==============
# Sources:
#   OpenAI: https://openai.com/api/pricing/  (May 2026)
#   Anthropic: https://docs.anthropic.com/   (May 2026)
#   Google Gemini: https://ai.google.dev/gemini-api/docs/pricing (May 2026)
#   DeepSeek: https://api-docs.deepseek.com/quick_start/pricing (May 2026)
#   Ollama: Free (local hardware)

PRICING_2026 = {
    # === OPENAI ===
    "OpenAI GPT-5.5":            (5.00,    30.00,   "OpenAI",    "270K",     "Frontier reasoning"),
    "OpenAI GPT-5.4":            (2.50,    15.00,   "OpenAI",    "270K",     "Strong reasoning"),
    "OpenAI GPT-5.4 mini":       (0.75,     4.50,   "OpenAI",    "270K",     "Fast, good quality"),
    "OpenAI GPT-4o":             (2.50,    10.00,   "OpenAI",    "128K",     "Vision + text"),
    "OpenAI GPT-4o-mini":        (0.15,     0.60,   "OpenAI",    "128K",     "Cheap, fast"),
    "OpenAI o1":                 (15.00,   60.00,   "OpenAI",    "200K",     "Deep reasoning"),
    "OpenAI o3":                 (10.00,   40.00,   "OpenAI",    "200K",     "Reasoning"),
    "OpenAI o4-mini":            (1.10,     4.40,   "OpenAI",    "200K",     "Fast reasoning"),

    # === ANTHROPIC ===
    "Claude Opus 4.7":           (5.00,    25.00,   "Anthropic", "1M",       "Most capable"),
    "Claude Sonnet 4.6":         (3.00,    15.00,   "Anthropic", "1M",       "Best speed/quality"),
    "Claude Haiku 4.5":          (1.00,     5.00,   "Anthropic", "200K",     "Fast, near-frontier"),
    "Claude Opus 4.6":           (5.00,    25.00,   "Anthropic", "1M",       "Previous Opus"),

    # === GOOGLE GEMINI ===
    "Gemini 3.5 Flash":          (1.50,     9.00,   "Google",    "1M+",      "Speed + intelligence"),
    "Gemini 3.5 Flash (Batch)":  (0.75,     4.50,   "Google",    "1M+",      "50% off batch"),
    "Gemini 3.1 Pro Preview":    (2.00,    12.00,   "Google",    "1M+",      "Pro quality"),
    "Gemini 3.1 Flash-Lite":     (0.25,     1.50,   "Google",    "1M+",      "Ultra cheap"),
    "Gemini 3.1 Flash-Lite Bat": (0.125,    0.75,   "Google",    "1M+",      "Batch 50% off"),
    "Gemini 2.5 Pro":            (2.70,    16.20,   "Google",    "1M+",      "Strong reasoning"),
    "Gemini 2.5 Flash":          (0.75,     4.50,   "Google",    "1M+",      "Fast"),

    # === DEEPSEEK ===
    "DeepSeek V4 Flash":         (0.14,     0.28,   "DeepSeek",  "1M",       "Best value"),
    "DeepSeek V4 Pro":           (0.435,    0.87,   "DeepSeek",  "1M",       "75% discount until May 31"),

    # === LOCAL (FREE) ===
    "Ollama llama3.1 8B":        (0.00,     0.00,   "Local",     "128K",     "Free, ~6GB VRAM"),
    "Ollama mistral 7B":         (0.00,     0.00,   "Local",     "32K",      "Free, ~5GB VRAM"),
    "Ollama qwen2.5 7B":         (0.00,     0.00,   "Local",     "128K",     "Free, multilingual"),
    "Ollama phi4 14B":           (0.00,     0.00,   "Local",     "16K",      "Free, ~10GB VRAM"),
}

# ============== TOKEN ESTIMATION ==============
CHARS_PER_TOKEN = 4.0

# ============== STATIC PROMPT (System Prompt) ==============
STATIC_SECTIONS = {
    "personality_block":       120,
    "anti_hallucination_rules": 650,
    "mechanics_guide":         800,
    "language_instruction":      120,
    "event_tracking_guide":     500,
    "diplomatic_mail_guide":    400,
    "intrigue_plot_guide":      400,
    "city_management_guide":    400,
    "enhanced_espionage_guide": 300,
    "multi_turn_planning_guide": 350,
    "combat_tactics_guide":     300,
    "available_actions_list":  800,
    "format_instruction":       60,
    "smart_targeting_guide":    400,
    "lore_tension_luxuries":    355,
}

STATIC_PROMPT_CHARS = sum(STATIC_SECTIONS.values())
STATIC_PROMPT_TOKENS = int(STATIC_PROMPT_CHARS / CHARS_PER_TOKEN)

# ============== DYNAMIC CONTEXT ESTIMATION ==============
def estimate_context_chars(num_cities=3, num_other_kingdoms=5, has_mail=False, has_intel=False,
                            num_trends=4, num_events=5, has_plan=False, num_surveys=0):
    header = 50 + 60
    cities = num_cities * 120 + num_cities * 60
    bldg = 120
    resources = 80
    culture = 100
    wars_allies = 60
    alerts = 40
    ruler = 80 + 40
    plots = 30
    surveys = num_surveys * 30
    mail = 150 if has_mail else 0
    diplomacy = 10 + num_other_kingdoms * 35
    intel = 10 + (2 if has_intel else 0) * 25
    trends = 10 + num_trends * 25
    policy = 50
    action_history = 100
    city_priorities = num_cities * 25
    plan_status = 80 if has_plan else 0
    combat = 30
    mechanics = 100
    event_log = 20 + num_events * 80
    world_laws = 400
    footer = 30
    total = (header + cities + bldg + resources + culture + wars_allies + alerts +
             ruler + plots + surveys + mail + diplomacy + intel + trends + policy +
             action_history + city_priorities + plan_status + combat + mechanics +
             event_log + world_laws + footer)
    return total


def calculate_cost(input_tokens, output_tokens, provider_name):
    input_price, output_price, _, _, _ = PRICING_2026[provider_name]
    return (input_tokens / 1_000_000) * input_price + (output_tokens / 1_000_000) * output_price


def main():
    print("=" * 90)
    print("  AIBOX TOKEN & COST CALCULATOR — 2026 EDITION")
    print("  All prices verified from official API docs (May 2026)")
    print("=" * 90)
    print()

    # --- Static prompt ---
    print("[1] STATIC SYSTEM PROMPT (sent every call)")
    print("-" * 70)
    for section, chars in STATIC_SECTIONS.items():
        print(f"  {section:.<45} {chars:>5} chars = {int(chars/CHARS_PER_TOKEN):>4} tokens")
    print(f"  {'TOTAL STATIC':.<45} {STATIC_PROMPT_CHARS:>5} chars = {STATIC_PROMPT_TOKENS:>4} tokens")
    print()

    # --- Dynamic context ---
    print("[2] DYNAMIC CONTEXT (BuildContextString — varies by kingdom size)")
    print("-" * 70)
    scenarios = [
        ("Tiny (1 city, 3 enemies)",      1, 3, False, False, 2, 2, False, 0),
        ("Small (2 cities, 4 enemies)", 2, 4, False, False, 3, 4, False, 0),
        ("Medium (3 cities, 5 enemies)",  3, 5, True,  True,  4, 6, True,  1),
        ("Large (5 cities, 7 enemies)",   5, 7, True,  True,  5, 8, True,  2),
        ("Empire (8 cities, 10 enemies)", 8, 10, True, True,  6, 10, True, 3),
    ]
    print(f"  {'Scenario':<32} {'Chars':>7} {'Tokens':>8} {'+System':>8} {'Total In':>9}")
    print("  " + "-" * 68)
    context_results = {}
    for name, cities, enemies, mail, intel, trends, events, plan, surveys in scenarios:
        ctx_chars = estimate_context_chars(cities, enemies, mail, intel, trends, events, plan, surveys)
        ctx_tokens = int(ctx_chars / CHARS_PER_TOKEN)
        total_input = STATIC_PROMPT_TOKENS + ctx_tokens
        print(f"  {name:<32} {ctx_chars:>7} {ctx_tokens:>8} {STATIC_PROMPT_TOKENS:>8} {total_input:>9}")
        context_results[name] = (ctx_tokens, total_input)
    print()

    typical_ctx_tokens, typical_total_input = context_results["Medium (3 cities, 5 enemies)"]

    # --- Per-kingdom cost ---
    print("[3] PER-KINGDOM, PER-TURN COST (Medium Kingdom, ~2263 input + 512 output tokens)")
    print("-" * 70)

    paid_models = [(n, d) for n, d in PRICING_2026.items() if d[2] != "Local"]
    local_models = [(n, d) for n, d in PRICING_2026.items() if d[2] == "Local"]

    rows = []
    for provider_name, (in_price, out_price, company, ctx, notes) in paid_models:
        cost = calculate_cost(typical_total_input, 512, provider_name)
        in_cost = (typical_total_input / 1_000_000) * in_price
        out_cost = (512 / 1_000_000) * out_price
        rows.append([
            company, provider_name, ctx, f"${in_price:.3f}", f"${out_price:.3f}",
            f"${cost:.6f}", notes
        ])

    # Sort by cost
    rows.sort(key=lambda r: float(r[5].replace("$", "")))

    print(tabulate(rows,
        headers=["Provider", "Model", "Ctx Win", "Input/1M", "Output/1M", "$/Turn", "Notes"],
        tablefmt="simple", stralign="left", numalign="right"))
    print()

    # --- Local models ---
    print("  LOCAL (FREE) MODELS:")
    print("  " + "-" * 66)
    for provider_name, (in_price, out_price, company, ctx, notes) in local_models:
        print(f"  {provider_name:<30} {ctx:>8}  {notes}")
    print()

    # --- Full world costs ---
    print("[4] FULL WORLD COSTS — 8 Kingdoms, 1 Hour Continuous Play")
    print("-" * 70)
    print("  Assumption: Rate limit = 30 calls/min (default). This caps total cost.")
    print("  With 30 calls/min, ALL world sizes cost the same per hour.")
    print()

    world_sizes = [("Small", 4), ("Medium", 8), ("Large", 12), ("Huge", 20)]

    # Use Medium scenario for context size
    adj_ctx = estimate_context_chars(3, 7, True, True, 4, 6, True, 1)
    adj_total_input = STATIC_PROMPT_TOKENS + int(adj_ctx / CHARS_PER_TOKEN)

    rows = []
    for world_name, num_kingdoms in world_sizes:
        total_input = adj_total_input
        total_output = 512

        # Rate limit: 30 calls/min max
        calls_per_min = 30
        cost_per_min_cheapest = calls_per_min * calculate_cost(total_input, total_output, "DeepSeek V4 Flash")
        cost_per_hour_cheap = cost_per_min_cheapest * 60
        cost_per_8hr_cheap = cost_per_hour_cheap * 8

        cost_per_min_exp = calls_per_min * calculate_cost(total_input, total_output, "Claude Opus 4.7")
        cost_per_hour_exp = cost_per_min_exp * 60
        cost_per_8hr_exp = cost_per_hour_exp * 8

        rows.append([
            world_name, num_kingdoms,
            f"${cost_per_min_cheapest:.4f}", f"${cost_per_hour_cheap:.2f}", f"${cost_per_8hr_cheap:.2f}",
            f"${cost_per_min_exp:.4f}", f"${cost_per_hour_exp:.2f}", f"${cost_per_8hr_exp:.2f}"
        ])

    print(tabulate(rows,
        headers=["World", "Kgdms", "Cheapest/min", "$/hr", "$/8hr", "Expensive/min", "$/hr", "$/8hr"],
        tablefmt="simple", stralign="left", numalign="right"))
    print("  Cheapest = DeepSeek V4 Flash  |  Most Expensive = Claude Opus 4.7")
    print()

    # --- Cost at different rate limits ---
    print("[5] COST VS RATE LIMIT (Medium World, 8 Kingdoms)")
    print("-" * 70)
    rate_limits = [10, 20, 30, 60, 120]
    rows = []
    for limit in rate_limits:
        per_min = limit * calculate_cost(adj_total_input, 512, "DeepSeek V4 Flash")
        per_hr = per_min * 60
        per_8hr = per_hr * 8
        rows.append([limit, f"${per_min:.4f}", f"${per_hr:.2f}", f"${per_8hr:.2f}"])

    print(tabulate(rows,
        headers=["Calls/Min", "DeepSeek/min", "$/hr", "$/8hr"],
        tablefmt="simple", stralign="left", numalign="right"))
    print()

    # --- Context window reality ---
    print("[6] CONTEXT WINDOW ANALYSIS")
    print("-" * 70)
    print(f"  Typical input per kingdom:  ~{typical_total_input} tokens")
    print(f"  Response budget:            ~512 tokens (default)")
    print(f"  Safety margin:             ~100 tokens")
    print()

    ctx_windows = [
        ("4K (old default)", 4096, "GPT-3.5 era"),
        ("8K", 8192, "GPT-4o-mini, Claude Haiku"),
        ("32K", 32768, "GPT-4o, Claude Sonnet"),
        ("128K", 128000, "GPT-5.4, Gemini 3.5, DeepSeek V4"),
        ("1M", 1000000, "Claude Opus 4.7, Gemini 3.5"),
    ]

    rows = []
    for name, size, models in ctx_windows:
        remaining = size - typical_total_input - 512 - 100
        turns_of_history = (size - STATIC_PROMPT_TOKENS) // typical_ctx_tokens
        rows.append([name, f"{size:,}", models, f"{remaining:,}", turns_of_history])

    print(tabulate(rows,
        headers=["Window", "Size", "Models", "Remaining", "Turns of History"],
        tablefmt="simple", stralign="left", numalign="right"))
    print()

    # --- Russian language impact ---
    print("[7] RUSSIAN LANGUAGE MULTIPLIER")
    print("-" * 70)
    print("  Russian/Cyrillic text is ~1.3x denser in tokens than English.")
    print(f"  English:  {typical_total_input} input + 512 output = {typical_total_input + 512} total tokens")
    russian_total = int((typical_total_input + 512) * 1.3)
    print(f"  Russian:  ~{russian_total} total tokens (+30%)")
    print()
    print("  Russian cost per kingdom per turn (DeepSeek V4 Flash):")
    print(f"    English: ${calculate_cost(typical_total_input, 512, 'DeepSeek V4 Flash'):.6f}")
    print(f"    Russian: ${calculate_cost(int(typical_total_input * 1.3), int(512 * 1.3), 'DeepSeek V4 Flash'):.6f}")
    print()

    # --- Recommendations ---
    print("[8] RECOMMENDATIONS BY BUDGET TIER")
    print("-" * 70)

    tiers = [
        ("FREE ($0/hr)", "Ollama + llama3.1 8B / mistral 7B", "128K ctx", "0.5s delay", "Unlimited gameplay"),
        ("BUDGET ($0.25-1/hr)", "DeepSeek V4 Flash / Gemini 3.1 Flash-Lite", "1M ctx", "2s delay", "Best value for money"),
        ("BALANCED ($1-5/hr)", "DeepSeek V4 Pro / GPT-4o-mini / Claude Haiku 4.5", "128K-1M", "1-2s delay", "Quality + speed"),
        ("PREMIUM ($5-25/hr)", "GPT-5.4 / Claude Sonnet 4.6 / Gemini 3.5 Flash", "128K-1M", "1s delay", "Top-tier reasoning"),
        ("LUXURY ($25+/hr)", "GPT-5.5 / Claude Opus 4.7", "270K-1M", "0.5s delay", "Maximum capability"),
    ]

    print(tabulate(tiers,
        headers=["Tier", "Models", "Context", "Delay", "Use Case"],
        tablefmt="simple", stralign="left"))
    print()

    # --- Ollama hardware requirements ---
    print("[9] OLLAMA HARDWARE REQUIREMENTS")
    print("-" * 70)
    ollama = [
        ("llama3.1 8B",     "~6 GB VRAM",  "128K", "35-55 t/s", "Great all-rounder"),
        ("llama3.1 70B",    "~40 GB VRAM", "128K", "10-15 t/s", "Very capable"),
        ("mistral 7B",      "~5 GB VRAM",  "32K",  "40-60 t/s", "Excellent reasoning"),
        ("mixtral 8x7B",    "~26 GB VRAM", "32K",  "15-25 t/s", "MoE architecture"),
        ("qwen2.5 7B",      "~5 GB VRAM",  "128K", "40-60 t/s", "Multilingual king"),
        ("qwen2.5 72B",     "~48 GB VRAM", "128K", "8-12 t/s",  "Top local model"),
        ("gemma2 9B",       "~6 GB VRAM",  "8K",   "35-50 t/s", "Google model"),
        ("gemma2 27B",      "~18 GB VRAM", "8K",   "15-20 t/s", "Strong reasoning"),
        ("deepseek-r1 7B",  "~5 GB VRAM",  "128K", "20-30 t/s", "Reasoning model"),
        ("deepseek-r1 32B", "~20 GB VRAM", "128K", "10-15 t/s", "Better reasoning"),
        ("phi4 14B",        "~10 GB VRAM", "16K",  "25-40 t/s", "Microsoft, very strong"),
        ("phi4 40B",        "~28 GB VRAM", "16K",  "8-12 t/s",  "Top-tier local"),
    ]
    print(tabulate(ollama,
        headers=["Model", "VRAM", "Context", "Speed", "Notes"],
        tablefmt="simple", stralign="left"))
    print()
    print("  Electricity: ~$0.04/hr for a 250W GPU @ $0.15/kWh")
    print()

    # --- Quick reference card ---
    print("[10] QUICK REFERENCE — COST PER HOUR (30 calls/min, 8 kingdoms)")
    print("-" * 70)

    ref_models = [
        "DeepSeek V4 Flash",
        "DeepSeek V4 Pro",
        "Gemini 3.1 Flash-Lite",
        "Gemini 3.5 Flash",
        "GPT-4o-mini",
        "GPT-5.4 mini",
        "Claude Haiku 4.5",
        "Claude Sonnet 4.6",
        "GPT-5.4",
        "Claude Opus 4.7",
        "GPT-5.5",
    ]

    rows = []
    for model in ref_models:
        if model in PRICING_2026:
            per_min = 30 * calculate_cost(adj_total_input, 512, model)
            per_hr = per_min * 60
            per_8hr = per_hr * 8
            rows.append([model, f"${per_min:.4f}", f"${per_hr:.2f}", f"${per_8hr:.2f}"])

    print(tabulate(rows,
        headers=["Model", "$/min", "$/hr", "$/8hr"],
        tablefmt="simple", stralign="left", numalign="right"))
    print()

    print("=" * 90)
    print("  NOTE: Prices may change. Always verify at official provider pages.")
    print("  DeepSeek V4 Pro discount (75% off) expires May 31, 2026.")
    print("=" * 90)


if __name__ == "__main__":
    main()
