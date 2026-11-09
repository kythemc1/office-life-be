using System.Collections.Concurrent;

namespace OfficeLifeOnline.Api.Features.Quests;

public sealed class QuestService
{
    private readonly object _lock = new();

    private readonly Dictionary<string, AssignmentState> _assignments = new();
    private readonly Dictionary<string, HashSet<string>> _playerAssignments = new();

    public QuestAssignment AssignIndividual(
        string roomId,
        string connectionId,
        string questId,
        QuestSource source)
    {
        var quest = QuestCatalog.Get(questId);

        lock (_lock)
        {
            var assignmentId = Guid.NewGuid().ToString("N");

            var state = new AssignmentState(
                assignmentId,
                quest,
                QuestScope.Individual,
                source,
                roomId,
                connectionId
            );

            state.Participants.Add(connectionId, new QuestParticipant(
                connectionId,
                QuestStatus.Pending,
                null,
                null,
                null
            ));

            _assignments.Add(assignmentId, state);
            AddPlayerAssignment(connectionId, assignmentId);

            return state.ToSnapshot();
        }
    }

    public QuestAssignment AssignShared(
        string roomId,
        IEnumerable<string> connectionIds,
        string questId,
        QuestSource source)
    {
        var quest = QuestCatalog.Get(questId);
        var participants = connectionIds.Distinct().ToArray();

        if (participants.Length == 0)
        {
            throw new InvalidOperationException(
                "Cannot assign a shared quest to an empty room."
            );
        }

        lock (_lock)
        {
            var assignmentId = Guid.NewGuid().ToString("N");

            var state = new AssignmentState(
                assignmentId,
                quest,
                QuestScope.Shared,
                source,
                roomId,
                null
            );

            foreach (var connectionId in participants)
            {
                state.Participants.Add(connectionId, new QuestParticipant(
                    connectionId,
                    QuestStatus.Pending,
                    null,
                    null,
                    null
                ));

                AddPlayerAssignment(connectionId, assignmentId);
            }

            _assignments.Add(assignmentId, state);

            return state.ToSnapshot();
        }
    }

    public IReadOnlyCollection<QuestAssignment> GetPlayerAssignments(
        string connectionId)
    {
        lock (_lock)
        {
            if (!_playerAssignments.TryGetValue(
                    connectionId,
                    out var assignmentIds))
            {
                return [];
            }

            return assignmentIds
                .Where(_assignments.ContainsKey)
                .Select(id => _assignments[id].ToSnapshotFor(connectionId))
                .ToArray();
        }
    }

    public QuestActionResult Accept(
        string connectionId,
        string assignmentId)
    {
        lock (_lock)
        {
            if (!TryGetParticipant(assignmentId, connectionId, out var state, out var participant))
            {
                return Failure("Không tìm thấy nhiệm vụ được giao.");
            }

            if (participant.Status != QuestStatus.Pending)
            {
                return Failure("Nhiệm vụ không còn ở trạng thái chờ.");
            }

            var now = DateTime.UtcNow;
            var updated = participant with
            {
                Status = QuestStatus.Accepted,
                AcceptedAt = now,
                DeadlineAt = now.AddSeconds(state.Quest.DurationSeconds)
            };

            state.Participants[connectionId] = updated;

            return Success(state, connectionId, "Đã nhận nhiệm vụ.");
        }
    }

    public QuestActionResult Reject(
        string connectionId,
        string assignmentId)
    {
        lock (_lock)
        {
            if (!TryGetParticipant(assignmentId, connectionId, out var state, out var participant))
            {
                return Failure("Không tìm thấy nhiệm vụ được giao.");
            }

            if (participant.Status != QuestStatus.Pending)
            {
                return Failure("Chỉ có thể từ chối nhiệm vụ đang chờ.");
            }

            state.Participants[connectionId] = participant with
            {
                Status = QuestStatus.Rejected
            };

            return Success(state, connectionId, "Đã từ chối nhiệm vụ.");
        }
    }

    public QuestActionResult Complete(
        string connectionId,
        string targetId)
    {
        lock (_lock)
        {
            var assignment = _playerAssignments.TryGetValue(
                connectionId,
                out var ids)
                ? ids.Select(id => _assignments.GetValueOrDefault(id))
                    .FirstOrDefault(state =>
                        state is not null &&
                        state.Quest.TargetId == targetId &&
                        state.Participants.TryGetValue(connectionId, out var p) &&
                        p.Status == QuestStatus.Accepted)
                : null;

            if (assignment is null)
            {
                return Failure("Bạn không có nhiệm vụ đang thực hiện tại vị trí này.");
            }

            var participant = assignment.Participants[connectionId];

            if (participant.DeadlineAt <= DateTime.UtcNow)
            {
                assignment.Participants[connectionId] = participant with
                {
                    Status = QuestStatus.Failed
                };

                return Success(assignment, connectionId, "Nhiệm vụ đã quá hạn.");
            }

            assignment.Participants[connectionId] = participant with
            {
                Status = QuestStatus.Completed,
                CompletedAt = DateTime.UtcNow
            };

            return Success(assignment, connectionId, "Hoàn thành nhiệm vụ.");
        }
    }

    public IReadOnlyCollection<QuestAssignment> ExpireOverdue()
    {
        lock (_lock)
        {
            var changed = new List<QuestAssignment>();
            var now = DateTime.UtcNow;

            foreach (var state in _assignments.Values)
            {
                foreach (var participant in state.Participants.Values.ToArray())
                {
                    if (participant.Status != QuestStatus.Accepted ||
                        participant.DeadlineAt is null ||
                        participant.DeadlineAt > now)
                    {
                        continue;
                    }

                    state.Participants[participant.ConnectionId] =
                        participant with { Status = QuestStatus.Failed };

                    changed.Add(state.ToSnapshot());
                }
            }

            return changed;
        }
    }

    public void RemovePlayer(string connectionId)
    {
        lock (_lock)
        {
            if (!_playerAssignments.Remove(connectionId, out var assignmentIds))
            {
                return;
            }

            foreach (var assignmentId in assignmentIds)
            {
                if (!_assignments.TryGetValue(assignmentId, out var state))
                {
                    continue;
                }

                state.Participants.Remove(connectionId);

                if (state.Participants.Count == 0)
                {
                    _assignments.Remove(assignmentId);
                }
            }
        }
    }

    private bool TryGetParticipant(
        string assignmentId,
        string connectionId,
        out AssignmentState state,
        out QuestParticipant participant)
    {
        if (_assignments.TryGetValue(assignmentId, out state!) &&
            state.Participants.TryGetValue(connectionId, out participant!))
        {
            return true;
        }

        state = null!;
        participant = null!;
        return false;
    }

    private void AddPlayerAssignment(string connectionId, string assignmentId)
    {
        if (!_playerAssignments.TryGetValue(connectionId, out var ids))
        {
            ids = [];
            _playerAssignments.Add(connectionId, ids);
        }

        ids.Add(assignmentId);
    }

    private static QuestActionResult Success(
        AssignmentState state,
        string connectionId,
        string message)
    {
        return new QuestActionResult(
            true,
            message,
            state.ToSnapshotFor(connectionId)
        );
    }

    private static QuestActionResult Failure(string message)
    {
        return new QuestActionResult(false, message, null);
    }

    private sealed class AssignmentState
    {
        public string AssignmentId { get; }
        public QuestDefinition Quest { get; }
        public QuestScope Scope { get; }
        public QuestSource Source { get; }
        public string RoomId { get; }
        public string? TargetConnectionId { get; }

        public Dictionary<string, QuestParticipant> Participants { get; } = new();

        public AssignmentState(
            string assignmentId,
            QuestDefinition quest,
            QuestScope scope,
            QuestSource source,
            string roomId,
            string? targetConnectionId)
        {
            AssignmentId = assignmentId;
            Quest = quest;
            Scope = scope;
            Source = source;
            RoomId = roomId;
            TargetConnectionId = targetConnectionId;
        }

        public QuestAssignment ToSnapshot() => new(
            AssignmentId,
            Quest.Id,
            Scope,
            Source,
            RoomId,
            TargetConnectionId,
            DateTime.UtcNow,
            Quest,
            Participants.Values.ToArray()
        );

        public QuestAssignment ToSnapshotFor(string connectionId) => new(
            AssignmentId,
            Quest.Id,
            Scope,
            Source,
            RoomId,
            TargetConnectionId,
            DateTime.UtcNow,
            Quest,
            Participants.TryGetValue(connectionId, out var participant)
                ? [participant]
                : []
        );
    }
}