namespace Heartbeat.Portal.Services;

public sealed record AssistantRequest(string Question, string Language);

public sealed record AssistantResponse(
    string Answer,
    DateTimeOffset GeneratedAt,
    string Provider,
    string Model,
    IReadOnlyList<string> SuggestedQuestions,
    string Disclaimer);
