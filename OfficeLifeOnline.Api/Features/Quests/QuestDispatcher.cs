using Microsoft.AspNetCore.SignalR;
using OfficeLifeOnline.Api.Hubs;
using OfficeLifeOnline.Api.Rooms;

namespace OfficeLifeOnline.Api.Features.Quests;

public sealed class QuestDispatcher
{
    private readonly QuestService _quests;
    private readonly RoomManager _rooms;
    private readonly IHubContext<GameHub> _hub;

    public QuestDispatcher(QuestService quests, RoomManager rooms, IHubContext<GameHub> hub)
    {
        _quests = quests;
        _rooms = rooms;
        _hub = hub;
    }

    public async Task AssignSystemQuestsAsync(string connectionId)
    {
        var roomId = _rooms.GetPlayerRoomId(connectionId)
            ?? throw new InvalidOperationException("Player is not in a room.");
        if (_rooms.GetRoom(roomId)?.Type != RoomType.Office)
            throw new InvalidOperationException("Player is not in an Office.");

        var assignments = _quests.AssignSystemQuests(roomId, connectionId);
        foreach (var assignment in assignments)
            await _hub.Clients.Client(connectionId).SendAsync("QuestAssignmentUpdated", assignment);
    }

    public async Task<QuestAssignment> AssignIndividualAsync(string connectionId, string questId, QuestSource source)
    {
        var roomId = _rooms.GetPlayerRoomId(connectionId)
            ?? throw new InvalidOperationException("Player is not in a room.");
        if (_rooms.GetRoom(roomId)?.Type != RoomType.Office)
            throw new InvalidOperationException("Player is not in an Office.");

        var assignment = _quests.AssignIndividual(roomId, connectionId, questId, source);
        await _hub.Clients.Client(connectionId).SendAsync("QuestAssignmentUpdated", assignment);
        return assignment;
    }

    public async Task<QuestAssignment> AssignSharedAsync(string roomId, string questId, QuestSource source)
    {
        var room = _rooms.GetRoom(roomId);
        if (room?.Type != RoomType.Office)
            throw new InvalidOperationException("Office Room not found.");

        var assignment = _quests.AssignShared(roomId,
            room.Players.Select(player => player.ConnectionId), questId, source);

        foreach (var player in room.Players)
        {
            var personal = _quests.GetPlayerAssignments(player.ConnectionId)
                .FirstOrDefault(x => x.AssignmentId == assignment.AssignmentId);
            if (personal is not null)
                await _hub.Clients.Client(player.ConnectionId).SendAsync("QuestAssignmentUpdated", personal);
        }
        return assignment;
    }
}