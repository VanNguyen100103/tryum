namespace Tryum.Api.Assistant;

public sealed class AiChatOptions
{
    public const string SectionName = "AI";

    public string GroqApiKey { get; set; } = string.Empty;
    public string GroqBaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";

    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string GeminiModel { get; set; } = "gemini-3.6-flash";
}
