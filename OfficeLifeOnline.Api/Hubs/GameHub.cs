using Microsoft.AspNetCore.SignalR;
using OfficeLifeOnline.Api.Features.Quests;
using OfficeLifeOnline.Api.Rooms;

namespace OfficeLifeOnline.Api.Hubs;

public sealed class GameHub : Hub
{
    private readonly RoomManager _roomManager;
    private readonly QuestService _questService;
    private readonly QuestDispatcher _questDispatcher;

    public GameHub(RoomManager roomManager, QuestService questService, QuestDispatcher questDispatcher)
    {
        _roomManager = roomManager;
        _questService = questService;
        _questDispatcher = questDispatcher;
    }

    public async Task<RoomSnapshot> JoinLobby(string? displayName = null)
    {
        var result = _roomManager.JoinLobby(Context.ConnectionId, NormalizeDisplayName(displayName));
        await Groups.AddToGroupAsync(Context.ConnectionId, result.Room.RoomId);
        await Clients.OthersInGroup(result.Room.RoomId).SendAsync("PlayerJoined", result.Player);
        return result.Room;
    }

    public async Task<RoomSnapshot> CreateOffice(string? displayName, string role)
    {
        var playerRole = ParseRole(role);
        var previousRoomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);
        var result = _roomManager.CreateOffice(Context.ConnectionId, NormalizeDisplayName(displayName), playerRole);
        await RemoveFromPreviousRoom(previousRoomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, result.Room.RoomId);
        await Clients.Caller.SendAsync("RoomJoined", result.Room);
        await _questDispatcher.AssignSystemQuestsAsync(Context.ConnectionId);
        await SendMyQuests();
        return result.Room;
    }

    public async Task<RoomSnapshot> JoinOffice(string roomId, string? displayName, string role)
    {
        var playerRole = ParseRole(role);
        var previousRoomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);
        var result = _roomManager.JoinOffice(Context.ConnectionId, roomId, NormalizeDisplayName(displayName), playerRole);
        await RemoveFromPreviousRoom(previousRoomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, result.Room.RoomId);
        await Clients.OthersInGroup(result.Room.RoomId).SendAsync("PlayerJoined", result.Player);
        await Clients.Caller.SendAsync("RoomJoined", result.Room);
        await _questDispatcher.AssignSystemQuestsAsync(Context.ConnectionId);
        await SendMyQuests();
        return result.Room;
    }

    public async Task<IReadOnlyCollection<QuestAssignment>> GetMyQuests()
    {
        EnsureInOffice();
        return _questService.GetPlayerAssignments(Context.ConnectionId);
    }

    public async Task<QuestActionResult> AcceptQuest(string assignmentId)
    {
        EnsureInOffice();
        var result = _questService.Accept(Context.ConnectionId, assignmentId);
        await NotifyQuestChanged(result.Assignment);
        return result;
    }

    public async Task<QuestActionResult> RejectQuest(string assignmentId)
    {
        EnsureInOffice();
        var result = _questService.Reject(Context.ConnectionId, assignmentId);
        await NotifyQuestChanged(result.Assignment);
        return result;
    }

    public async Task<QuestActionResult> CompleteQuest(string targetId)
    {
        EnsureInOffice();
        var roomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);
        var player = _roomManager.GetPlayer(Context.ConnectionId);
        if (roomId is null || player is null)
            return new QuestActionResult(false, "Không tìm thấy vị trí người chơi.", null);

        var result = _questService.Complete(Context.ConnectionId, roomId, targetId, player);
        await NotifyQuestChanged(result.Assignment);
        return result;
    }

    public async Task UpdatePosition(float x, float y, float z, float rotationY)
    {
        var roomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);
        if (roomId is null) return;
        if (!_roomManager.UpdatePlayerPosition(Context.ConnectionId, x, y, z, rotationY)) return;

        await Clients.OthersInGroup(roomId).SendAsync("PlayerMoved",
            new PlayerPositionUpdate(Context.ConnectionId, x, y, z, rotationY));
    }

    public Task LeaveRoom() => LeaveCurrentRoom();

    public Task<IReadOnlyCollection<RoomSnapshot>> GetPublicOfficeRooms() =>
        Task.FromResult(_roomManager.GetPublicOfficeRooms());

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentRoom();
        await base.OnDisconnectedAsync(exception);
    }

    private void EnsureInOffice()
    {
        var roomId = _roomManager.GetPlayerRoomId(Context.ConnectionId);
        if (roomId is null || _roomManager.GetRoom(roomId)?.Type != RoomType.Office)
            throw new HubException("Thao tác này chỉ thực hiện trong Office.");
    }

    private async Task SendMyQuests() =>
        await Clients.Caller.SendAsync("MyQuestsUpdated",
            _questService.GetPlayerAssignments(Context.ConnectionId));

    private async Task NotifyQuestChanged(QuestAssignment? assignment)
    {
        if (assignment is null) return;

        var recipients = _questService.GetAssignmentParticipants(assignment.AssignmentId);
        foreach (var connectionId in recipients)
        {
            var personal = _questService.GetPlayerAssignments(connectionId)
                .FirstOrDefault(x => x.AssignmentId == assignment.AssignmentId);
            if (personal is not null)
                await Clients.Client(connectionId).SendAsync("QuestAssignmentUpdated", personal);
        }
    }

    private async Task RemoveFromPreviousRoom(string? previousRoomId)
    {
        if (string.IsNullOrWhiteSpace(previousRoomId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousRoomId);
        await Clients.Group(previousRoomId).SendAsync("PlayerLeft", Context.ConnectionId);
    }

    private async Task LeaveCurrentRoom()
    {
        var result = _roomManager.LeaveRoom(Context.ConnectionId);
        if (result is null) return;
        _questService.RemovePlayer(Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, result.RoomId);
        await Clients.Group(result.RoomId).SendAsync("PlayerLeft", Context.ConnectionId);
        if (result.RoomClosed)
            await Clients.All.SendAsync("OfficeRoomClosed", result.RoomId);
    }

    private static PlayerRole ParseRole(string role)
    {
        if (!Enum.TryParse<PlayerRole>(role, false, out var parsed) || !Enum.IsDefined(parsed))
            throw new HubException("Invalid player role.");
        return parsed;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Player";
        var value = displayName.Trim();
        return value[..Math.Min(value.Length, 24)];
    }
}
