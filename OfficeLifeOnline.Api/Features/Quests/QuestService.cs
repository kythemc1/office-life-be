using OfficeLifeOnline.Api.Rooms;

namespace OfficeLifeOnline.Api.Features.Quests;

public sealed class QuestService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AssignmentState> _assignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _playerAssignments = new(StringComparer.Ordinal);

    public IReadOnlyCollection<QuestAssignment> AssignSystemQuests(string roomId, string connectionId)
    {
        lock (_sync)
        {
            var alreadyAssigned = _playerAssignments.TryGetValue(connectionId, out var ids)
                ? ids.Select(id => _assignments.GetValueOrDefault(id))
                    .Where(x => x is not null && x.Source == QuestSource.System)
                    .Select(x => x!.Definition.Id).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            var created = new List<QuestAssignment>();
            foreach (var definition in QuestCatalog.SystemAssignable)
            {
                if (alreadyAssigned.Contains(definition.Id)) continue;
                var state = new AssignmentState(Guid.NewGuid().ToString("N"), definition,
                    QuestScope.Individual, QuestSource.System, roomId, connectionId, DateTime.UtcNow);
                state.Participants[connectionId] = NewParticipant(connectionId);
                _assignments.Add(state.Id, state);
                AddPlayerAssignment(connectionId, state.Id);
                created.Add(state.SnapshotFor(connectionId));
            }
            return created;
        }
    }

    public QuestAssignment AssignIndividual(string roomId, string connectionId, string questId, QuestSource source)
    {
        var definition = QuestCatalog.Get(questId);
        lock (_sync)
        {
            var state = new AssignmentState(Guid.NewGuid().ToString("N"), definition,
                QuestScope.Individual, source, roomId, connectionId, DateTime.UtcNow);
            state.Participants[connectionId] = NewParticipant(connectionId);
            _assignments.Add(state.Id, state);
            AddPlayerAssignment(connectionId, state.Id);
            return state.SnapshotFor(connectionId);
        }
    }

    public QuestAssignment AssignShared(string roomId, IEnumerable<string> connectionIds, string questId, QuestSource source)
    {
        var definition = QuestCatalog.Get(questId);
        if (string.IsNullOrWhiteSpace(definition.TargetId))
            throw new InvalidOperationException("Cannot assign a quest without an interaction target.");

        var recipients = connectionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (recipients.Length == 0)
            throw new InvalidOperationException("Cannot assign a shared quest to an empty room.");

        lock (_sync)
        {
            var state = new AssignmentState(Guid.NewGuid().ToString("N"), definition,
                QuestScope.Shared, source, roomId, null, DateTime.UtcNow);
            foreach (var id in recipients)
            {
                state.Participants[id] = NewParticipant(id);
                AddPlayerAssignment(id, state.Id);
            }
            _assignments.Add(state.Id, state);
            return state.Snapshot();
        }
    }

    public IReadOnlyCollection<QuestAssignment> GetPlayerAssignments(string connectionId)
    {
        lock (_sync)
        {
            if (!_playerAssignments.TryGetValue(connectionId, out var ids)) return [];
            return ids.Where(_assignments.ContainsKey)
                .Select(id => _assignments[id].SnapshotFor(connectionId)).ToArray();
        }
    }

    public IReadOnlyCollection<string> GetAssignmentParticipants(string assignmentId)
    {
        lock (_sync)
        {
            return _assignments.TryGetValue(assignmentId, out var state)
                ? state.Participants.Keys.ToArray()
                : [];
        }
    }

    public QuestActionResult Accept(string connectionId, string assignmentId) =>
        ChangePending(connectionId, assignmentId, accept: true);

    public QuestActionResult Reject(string connectionId, string assignmentId) =>
        ChangePending(connectionId, assignmentId, accept: false);

    private QuestActionResult ChangePending(string connectionId, string assignmentId, bool accept)
    {
        lock (_sync)
        {
            if (!TryGetParticipant(connectionId, assignmentId, out var state, out var participant))
                return Fail("Không tìm thấy nhiệm vụ được giao.");
            if (participant.Status != QuestStatus.Pending)
                return Fail("Nhiệm vụ không còn ở trạng thái chờ.");

            var now = DateTime.UtcNow;
            state.Participants[connectionId] = participant with
            {
                Status = accept ? QuestStatus.Accepted : QuestStatus.Rejected,
                AcceptedAt = accept ? now : null,
                DeadlineAt = accept ? now.AddSeconds(state.Definition.DurationSeconds) : null
            };
            return Ok(state, connectionId, accept ? "Đã nhận nhiệm vụ." : "Đã từ chối nhiệm vụ.");
        }
    }

    public QuestActionResult Complete(string connectionId, string roomId, string targetId, PlayerInfo player)
    {
        if (!QuestTargetCatalog.IsWithinRange(targetId, player.X, player.Z))
            return Fail("Bạn cần đến gần đúng vị trí tương tác để hoàn thành nhiệm vụ.");

        lock (_sync)
        {
            if (!_playerAssignments.TryGetValue(connectionId, out var ids))
                return Fail("Bạn chưa được giao nhiệm vụ tại vị trí này.");

            foreach (var id in ids)
            {
                if (!_assignments.TryGetValue(id, out var state) ||
                    state.RoomId != roomId ||
                    !string.Equals(state.Definition.TargetId, targetId, StringComparison.Ordinal))
                    continue;

                if (!state.Participants.TryGetValue(connectionId, out var participant) ||
                    participant.Status != QuestStatus.Accepted)
                    continue;

                var now = DateTime.UtcNow;
                if (participant.DeadlineAt is null || participant.DeadlineAt <= now)
                {
                    state.Participants[connectionId] = participant with { Status = QuestStatus.Failed };
                    return Ok(state, connectionId, "Nhiệm vụ đã quá hạn.");
                }

                state.Participants[connectionId] = participant with
                {
                    Status = QuestStatus.Completed,
                    CompletedAt = now
                };
                return Ok(state, connectionId, "Hoàn thành nhiệm vụ.");
            }
            return Fail("Bạn không có nhiệm vụ đang thực hiện tại vị trí này.");
        }
    }

    public IReadOnlyCollection<QuestExpiry> ExpireOverdue()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var expired = new List<QuestExpiry>();
            foreach (var state in _assignments.Values)
            {
                var changed = false;
                foreach (var pair in state.Participants.ToArray())
                {
                    var participant = pair.Value;
                    if (participant.Status != QuestStatus.Accepted ||
                        participant.DeadlineAt is null || participant.DeadlineAt > now)
                        continue;
                    state.Participants[pair.Key] = participant with { Status = QuestStatus.Failed };
                    changed = true;
                }
                if (changed) expired.Add(new QuestExpiry(state.RoomId, state.Snapshot()));
            }
            return expired;
        }
    }

    public void RemovePlayer(string connectionId)
    {
        lock (_sync)
        {
            if (!_playerAssignments.Remove(connectionId, out var ids)) return;
            foreach (var id in ids)
            {
                if (!_assignments.TryGetValue(id, out var state)) continue;
                state.Participants.Remove(connectionId);
                if (state.Participants.Count == 0) _assignments.Remove(id);
            }
        }
    }

    private bool TryGetParticipant(string connectionId, string assignmentId,
        out AssignmentState state, out QuestParticipant participant)
    {
        if (_assignments.TryGetValue(assignmentId, out state!) &&
            state.Participants.TryGetValue(connectionId, out participant!)) return true;
        state = null!;
        participant = null!;
        return false;
    }

    private void AddPlayerAssignment(string playerId, string assignmentId)
    {
        if (!_playerAssignments.TryGetValue(playerId, out var ids))
            _playerAssignments[playerId] = ids = new HashSet<string>(StringComparer.Ordinal);
        ids.Add(assignmentId);
    }

    private static QuestParticipant NewParticipant(string id) =>
        new(id, QuestStatus.Pending, null, null, null);

    private static QuestActionResult Fail(string message) => new(false, message, null);
    private static QuestActionResult Ok(AssignmentState state, string playerId, string message) =>
        new(true, message, state.SnapshotFor(playerId));

    private sealed class AssignmentState
    {
        public string Id { get; }
        public QuestDefinition Definition { get; }
        public QuestScope Scope { get; }
        public QuestSource Source { get; }
        public string RoomId { get; }
        public string? TargetConnectionId { get; }
        public DateTime AssignedAt { get; }
        public Dictionary<string, QuestParticipant> Participants { get; } = new(StringComparer.Ordinal);

        public AssignmentState(string id, QuestDefinition definition, QuestScope scope, QuestSource source,
            string roomId, string? targetConnectionId, DateTime assignedAt)
        {
            Id = id; Definition = definition; Scope = scope; Source = source;
            RoomId = roomId; TargetConnectionId = targetConnectionId; AssignedAt = assignedAt;
        }

        private (int Completed, int Total) SharedProgress() =>
            Scope == QuestScope.Shared
                ? (Participants.Values.Count(x => x.Status == QuestStatus.Completed), Participants.Count)
                : (0, 0);

        public QuestAssignment Snapshot()
        {
            var progress = SharedProgress();
            return new QuestAssignment(Id, Definition.Id, Scope, Source, RoomId,
                TargetConnectionId, AssignedAt, Definition, Participants.Values.ToArray(),
                progress.Completed, progress.Total);
        }

        public QuestAssignment SnapshotFor(string playerId)
        {
            var progress = SharedProgress();
            return new QuestAssignment(Id, Definition.Id, Scope, Source, RoomId,
                TargetConnectionId, AssignedAt, Definition,
                Participants.TryGetValue(playerId, out var participant) ? [participant] : [],
                progress.Completed, progress.Total);
        }
    }
}
