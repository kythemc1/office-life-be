using Microsoft.AspNetCore.SignalR;
using OfficeLifeOnline.Api.Features.Quests;
using OfficeLifeOnline.Api.Rooms;

namespace OfficeLifeOnline.Api.Hubs;

public sealed class GameHub : Hub
{
    private readonly RoomManager _roomManager;
    private readonly QuestService _questService;

    public GameHub(
        RoomManager roomManager,
        QuestService questService)
    {
        _roomManager = roomManager;
        _questService = questService;
    }

    public async Task<RoomSnapshot> JoinLobby(string? displayName = null)
    {
        var result = _roomManager.JoinLobby(
            Context.ConnectionId,
            NormalizeDisplayName(displayName)
        );

        await Groups.AddToGroupAsync(Context.ConnectionId, result.Room.RoomId);

        await Clients.OthersInGroup(result.Room.RoomId)
            .SendAsync("PlayerJoined", result.Player);

        return result.Room;
    }

    public async Task<RoomSnapshot> CreateOffice(
        string? displayName,
        string role)
    {
        var playerRole = ParseRole(role);
        var previousRoomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);

        var result = _roomManager.CreateOffice(
            Context.ConnectionId,
            NormalizeDisplayName(displayName),
            playerRole
        );

        await RemoveFromPreviousRoom(previousRoomId);

        await Groups.AddToGroupAsync(Context.ConnectionId, result.Room.RoomId);
        await Clients.Caller.SendAsync("RoomJoined", result.Room);

        await SendMyQuests();

        return result.Room;
    }

    public async Task<RoomSnapshot> JoinOffice(
        string roomId,
        string? displayName,
        string role)
    {
        var playerRole = ParseRole(role);
        var previousRoomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);

        var result = _roomManager.JoinOffice(
            Context.ConnectionId,
            roomId,
            NormalizeDisplayName(displayName),
            playerRole
        );

        await RemoveFromPreviousRoom(previousRoomId);

        await Groups.AddToGroupAsync(Context.ConnectionId, result.Room.RoomId);

        await Clients.OthersInGroup(result.Room.RoomId)
            .SendAsync("PlayerJoined", result.Player);

        await Clients.Caller.SendAsync("RoomJoined", result.Room);

        await SendMyQuests();

        return result.Room;
    }

    public async Task<IReadOnlyCollection<QuestAssignment>> GetMyQuests()
    {
        var roomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);

        if (roomId is null)
        {
            throw new HubException("Bạn chưa ở trong phòng.");
        }

        return _questService.GetPlayerAssignments(Context.ConnectionId);
    }

    public async Task<QuestActionResult> AcceptQuest(string assignmentId)
    {
        EnsureInOffice();

        var result = _questService.Accept(Context.ConnectionId, assignmentId);

        if (result.Assignment is not null)
        {
            await NotifyQuestChanged(result.Assignment);
        }

        return result;
    }

    public async Task<QuestActionResult> RejectQuest(string assignmentId)
    {
        EnsureInOffice();

        var result = _questService.Reject(Context.ConnectionId, assignmentId);

        if (result.Assignment is not null)
        {
            await NotifyQuestChanged(result.Assignment);
        }

        return result;
    }

    public async Task<QuestActionResult> CompleteQuest(string targetId)
    {
        EnsureInOffice();

        var result = _questService.Complete(Context.ConnectionId, targetId);

        if (result.Assignment is not null)
        {
            await NotifyQuestChanged(result.Assignment);
        }

        return result;
    }

    public async Task UpdatePosition(
        float x,
        float y,
        float z,
        float rotationY)
    {
        var roomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);

        if (roomId is null)
        {
            return;
        }

        var updated = _roomManager.UpdatePlayerPosition(
            Context.ConnectionId,
            x,
            y,
            z,
            rotationY
        );

        if (!updated)
        {
            return;
        }

        await Clients.OthersInGroup(roomId).SendAsync(
            "PlayerMoved",
            new PlayerPositionUpdate(
                Context.ConnectionId,
                x,
                y,
                z,
                rotationY
            )
        );
    }

    public async Task LeaveRoom()
    {
        await LeaveCurrentRoom();
    }

    public Task<IReadOnlyCollection<RoomSnapshot>> GetPublicOfficeRooms()
    {
        return Task.FromResult(_roomManager.GetPublicOfficeRooms());
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentRoom();
        await base.OnDisconnectedAsync(exception);
    }

    private void EnsureInOffice()
    {
        var roomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);
        var room = roomId is null ? null : _roomManager.GetRoom(roomId);

        if (room?.Type != RoomType.Office)
        {
            throw new HubException("Thao tác này chỉ thực hiện trong Office.");
        }
    }

    private async Task SendMyQuests()
    {
        var quests = _questService.GetPlayerAssignments(Context.ConnectionId);

        await Clients.Caller.SendAsync("MyQuestsUpdated", quests);
    }

    private async Task NotifyQuestChanged(QuestAssignment assignment)
    {
        if (assignment.Scope == QuestScope.Shared)
        {
            // Shared assignment: notify only members of its Office Room.
            await Clients.Group(assignment.RoomId)
                .SendAsync("QuestAssignmentUpdated", assignment);
            return;
        }

        // Individual assignment: notify only its assigned player.
        if (assignment.TargetConnectionId is not null)
        {
            await Clients.Client(assignment.TargetConnectionId)
                .SendAsync("QuestAssignmentUpdated", assignment);
        }
    }

    private async Task RemoveFromPreviousRoom(string? previousRoomId)
    {
        if (string.IsNullOrWhiteSpace(previousRoomId))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousRoomId);

        await Clients.Group(previousRoomId).SendAsync(
            "PlayerLeft",
            Context.ConnectionId
        );
    }

    private async Task LeaveCurrentRoom()
    {
        var result = _roomManager.LeaveRoom(Context.ConnectionId);

        if (result is null)
        {
            return;
        }

        _questService.RemovePlayer(Context.ConnectionId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, result.RoomId);

        await Clients.Group(result.RoomId).SendAsync(
            "PlayerLeft",
            Context.ConnectionId
        );

        if (result.RoomClosed)
        {
            await Clients.All.SendAsync("OfficeRoomClosed", result.RoomId);
        }
    }

    private static PlayerRole ParseRole(string role)
    {
        if (!Enum.TryParse<PlayerRole>(
                role,
                ignoreCase: false,
                out var playerRole) ||
            !Enum.IsDefined(playerRole))
        {
            throw new HubException("Invalid player role.");
        }

        return playerRole;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Player";
        }

        var normalized = displayName.Trim();

        return normalized[..Math.Min(normalized.Length, 24)];
    }
}