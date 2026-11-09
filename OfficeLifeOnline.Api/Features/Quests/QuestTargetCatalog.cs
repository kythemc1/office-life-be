namespace OfficeLifeOnline.Api.Features.Quests;

public sealed record QuestTarget(string Id, float X, float Z, float MaxDistance);

public static class QuestTargetCatalog
{
    // X/Z match the registered InteractionTarget positions in the Office scene.
    // The server checks the latest position reported through GameHub.UpdatePosition.
    private static readonly IReadOnlyDictionary<string, QuestTarget> Targets =
        new Dictionary<string, QuestTarget>(StringComparer.Ordinal)
        {
            ["coffee"] = new("coffee", -8.3f, -4.35f, 3.0f),
            ["printer"] = new("printer", 8.4f, 1.35f, 3.0f),
            ["meeting"] = new("meeting", 6.0f, -4.0f, 3.0f),
            ["report"] = new("report", 6.6f, 2.2f, 3.0f)
        };

    public static bool IsWithinRange(string targetId, float playerX, float playerZ)
    {
        if (!Targets.TryGetValue(targetId, out var target)) return false;
        var dx = playerX - target.X;
        var dz = playerZ - target.Z;
        return (dx * dx) + (dz * dz) <= target.MaxDistance * target.MaxDistance;
    }
}
