namespace OfficeLifeOnline.Api.Features.Quests;

public enum QuestScope
{
    Individual,
    Shared
}

public enum QuestStatus
{
    Pending,
    Accepted,
    Rejected,
    Completed,
    Failed
}

public enum QuestSource
{
    System,
    TeamLeader,
    Director,
    ReceptionistNpc
}

public sealed record QuestDefinition(
    string Id,
    string Title,
    string Description,
    string TargetId,
    int DurationSeconds,
    int RewardXp,
    int PenaltyXp
);

public sealed record QuestParticipant(
    string ConnectionId,
    QuestStatus Status,
    DateTime? AcceptedAt,
    DateTime? DeadlineAt,
    DateTime? CompletedAt
);

public sealed record QuestAssignment(
    string AssignmentId,
    string QuestId,
    QuestScope Scope,
    QuestSource Source,
    string RoomId,
    string? TargetConnectionId,
    DateTime AssignedAt,
    QuestDefinition Quest,
    IReadOnlyCollection<QuestParticipant> Participants
);

public sealed record QuestActionResult(
    bool Success,
    string Message,
    QuestAssignment? Assignment
);

public sealed record QuestProgressUpdate(
    string AssignmentId,
    string QuestId,
    string TargetId,
    QuestStatus Status,
    string ConnectionId
);