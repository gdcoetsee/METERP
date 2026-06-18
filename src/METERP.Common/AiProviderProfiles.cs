namespace METERP.Common;

/// <summary>
/// Preset OpenAI-compatible AI provider endpoints and default models.
/// METERP uses the OpenAI /chat/completions shape — not native provider URLs (e.g. Gemini generateContent).
/// </summary>
public static class AiProviderProfiles
{
    public const string GoogleGemini = "Google Gemini (Free)";
    public const string Groq = "Groq (Free tier)";
    public const string Ollama = "Ollama (Local, Free)";
    public const string OpenRouter = "OpenRouter";
    public const string Grok = "Grok (xAI)";
    public const string OpenAi = "OpenAI";
    public const string Custom = "Custom";

    /// <summary>All presets; free-tier options listed first.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        GoogleGemini,
        Groq,
        Ollama,
        OpenRouter,
        Grok,
        OpenAi,
        Custom
    ];

    public static (string BaseUrl, string Model, string Hint) GetPreset(string provider) =>
        provider switch
        {
            GoogleGemini => (
                "https://generativelanguage.googleapis.com/v1beta/openai",
                "gemini-flash-latest",
                "Google AI Studio key (AIza...). Uses OpenAI-compatible endpoint — not the generateContent curl URL."),
            Groq => (
                "https://api.groq.com/openai/v1",
                "llama-3.3-70b-versatile",
                "Groq free tier — fast open models; sign up at console.groq.com"),
            Ollama => (
                "http://localhost:11434/v1",
                "llama3",
                "100% free local inference — install Ollama and run: ollama pull llama3"),
            OpenRouter => (
                "https://openrouter.ai/api/v1",
                "google/gemini-2.0-flash-exp:free",
                "OpenRouter — includes free model slugs; requires OpenRouter API key"),
            Grok => (
                "https://api.x.ai/v1",
                "grok-3-mini",
                "xAI developer API (xai- key) — requires purchased API credits; X Premium does not include this"),
            OpenAi => (
                "https://api.openai.com/v1",
                "gpt-4o-mini",
                "OpenAI — use an sk- API key"),
            _ => ("", "", "Enter your own OpenAI-compatible base URL and model")
        };

    public static bool IsFreeTierPreset(string provider) =>
        provider is GoogleGemini or Groq or Ollama
        || (provider == OpenRouter);
}