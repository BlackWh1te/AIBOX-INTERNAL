using System;

namespace AIBoxInternal.Core
{
    public enum AIProvider { Internal, Ollama, OpenAI, Claude }
    public enum GameLanguage { English, Russian, Spanish, Chinese, German }

    [Serializable]
    public class KingdomConfig
    {
        public AIProvider Provider = AIProvider.Internal;
        public string Model = "llama3";
        public string ApiKey = "";
        public string Endpoint = "http://localhost:11434/api/generate";
        public string CustomSystemPrompt = "";

        // --- Rate Limiting ---
        public float MinDelayBetweenCalls = 2f;
        public int MaxCallsPerMinute = 30;

        // --- Context Window ---
        public int ContextWindowTokens = 4096;
        public int MaxResponseTokens = 512;

        // --- Token Budget ---
        public bool EnableTokenBudget = false;
        public int MaxTokensPerMinute = 4000;
    }

    [Serializable]
    public class GlobalAIConfig
    {
        public AIProvider Provider = AIProvider.Internal;
        public string Model = "llama3";
        public string ApiKey = "";
        public string Endpoint = "http://localhost:11434/api/generate";
        public string CustomSystemPrompt = "";

        // --- Rate Limiting ---
        public float MinDelayBetweenCalls = 2f;
        public int MaxCallsPerMinute = 30;

        // --- Context Window ---
        public int ContextWindowTokens = 4096;
        public int MaxResponseTokens = 512;

        // --- Token Budget ---
        public bool EnableTokenBudget = false;
        public int MaxTokensPerMinute = 4000;

        public KingdomConfig ToKingdomConfig()
        {
            return new KingdomConfig
            {
                Provider = this.Provider,
                Model = this.Model,
                ApiKey = this.ApiKey,
                Endpoint = this.Endpoint,
                CustomSystemPrompt = this.CustomSystemPrompt,
                MinDelayBetweenCalls = this.MinDelayBetweenCalls,
                MaxCallsPerMinute = this.MaxCallsPerMinute,
                ContextWindowTokens = this.ContextWindowTokens,
                MaxResponseTokens = this.MaxResponseTokens,
                EnableTokenBudget = this.EnableTokenBudget,
                MaxTokensPerMinute = this.MaxTokensPerMinute
            };
        }
    }

    public static class GlobalSettings
    {
        public static GameLanguage Language = GameLanguage.English;
        public static bool UseGlobalAI = true; // true = one global config for all kingdoms
        public static GlobalAIConfig GlobalAI = new GlobalAIConfig();
    }
}
