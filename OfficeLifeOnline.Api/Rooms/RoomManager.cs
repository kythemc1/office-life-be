
namespace OfficeLifeOnline.Api.Rooms;

public sealed class RoomManager
{
    public const string SharedLobbyId = "shared-lobby";
    public const int SharedLobbyCapacity = 50;

    private readonly object _lock = new();

    private readonly Dictionary<string, GameRoom> _rooms = new();
    private readonly Dictionary<string, string> _playerRooms = new();

    public RoomManager()
    {
        _rooms.Add(
            SharedLobbyId,
            new GameRoom(
                SharedLobbyId,
                "Shared Lobby",
                RoomType.SharedLobby,
                isPublic: false
            )
        );
    }

    public RoomJoinResult JoinLobby(
        string connectionId,
        string displayName)
    {
        lock (_lock)
        {
            if (_playerRooms.TryGetValue(connectionId, out var currentRoomId))
            {
                if (currentRoomId == SharedLobbyId)
                {
                    var existingRoom = _rooms[SharedLobbyId];
                    var existingPlayer = existingRoom.Players[connectionId];

                    return new RoomJoinResult(
                        CreateSnapshot(existingRoom),
                        existingPlayer
                    );
                }

                throw new InvalidOperationException(
                    "Player must leave the current room before joining Lobby."
                );
            }

            var lobby = _rooms[SharedLobbyId];

            if (lobby.Players.Count >= SharedLobbyCapacity)
            {
                throw new InvalidOperationException("Shared Lobby is full.");
            }

            var player = CreatePlayer(connectionId, displayName, null);

            lobby.Players.Add(connectionId, player);
            _playerRooms.Add(connectionId, SharedLobbyId);

            return new RoomJoinResult(CreateSnapshot(lobby), player);
        }
    }

    public RoomJoinResult CreateOffice(
        string connectionId,
        string displayName,
        PlayerRole role)
    {
        lock (_lock)
        {
            EnsureCanEnterOffice(connectionId);

            var roomId = Guid.NewGuid().ToString("N");

            var room = new GameRoom(
                roomId,
                $"Office {roomId[..6].ToUpperInvariant()}",
                RoomType.Office,
                isPublic: true
            );

            var player = CreatePlayer(connectionId, displayName, role);

            room.Players.Add(connectionId, player);
            _rooms.Add(roomId, room);
            _playerRooms[connectionId] = roomId;

            return new RoomJoinResult(CreateSnapshot(room), player);
        }
    }

    public RoomJoinResult JoinOffice(
        string connectionId,
        string roomId,
        string displayName,
        PlayerRole role)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(roomId, out var room) ||
                room.Type != RoomType.Office ||
                !room.IsPublic)
            {
                throw new KeyNotFoundException(
                    "Office Room does not exist or is not public."
                );
            }

            EnsureCanEnterOffice(connectionId);

            var player = CreatePlayer(connectionId, displayName, role);

            room.Players.Add(connectionId, player);
            _playerRooms[connectionId] = roomId;

            return new RoomJoinResult(CreateSnapshot(room), player);
        }
    }

    public bool UpdatePlayerPosition(
        string connectionId,
        float x,
        float y,
        float z,
        float rotationY)
    {
        if (!float.IsFinite(x) ||
            !float.IsFinite(y) ||
            !float.IsFinite(z) ||
            !float.IsFinite(rotationY))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_playerRooms.TryGetValue(connectionId, out var roomId) ||
                !_rooms.TryGetValue(roomId, out var room) ||
                !room.Players.TryGetValue(connectionId, out var player))
            {
                return false;
            }

            room.Players[connectionId] = player with
            {
                X = x,
                Y = y,
                Z = z,
                RotationY = rotationY
            };

            return true;
        }
    }

    public RoomLeaveResult? LeaveRoom(string connectionId)
    {
        lock (_lock)
        {
            if (!_playerRooms.TryGetValue(connectionId, out var roomId))
            {
                return null;
            }

            if (!_rooms.TryGetValue(roomId, out var room))
            {
                _playerRooms.Remove(connectionId);
                return null;
            }

            if (!room.Players.Remove(connectionId, out var player))
            {
                _playerRooms.Remove(connectionId);
                return null;
            }

            _playerRooms.Remove(connectionId);

            var roomClosed = false;

            if (room.Type == RoomType.Office && room.Players.Count == 0)
            {
                _rooms.Remove(roomId);
                roomClosed = true;
            }

            return new RoomLeaveResult(
                roomId,
                room.Type,
                player.DisplayName,
                roomClosed,
                room.Players.Values.ToArray()
            );
        }
    }

    public IReadOnlyCollection<RoomSnapshot> GetPublicOfficeRooms()
    {
        lock (_lock)
        {
            return _rooms.Values
                .Where(room =>
                    room.Type == RoomType.Office &&
                    room.IsPublic
                )
                .Select(CreateSnapshot)
                .ToArray();
        }
    }

    public RoomSnapshot? GetRoom(string roomId)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(roomId, out var room)
                ? CreateSnapshot(room)
                : null;
        }
    }

    public string? GetPlayerRoomId(string connectionId)
    {
        lock (_lock)
        {
            return _playerRooms.TryGetValue(connectionId, out var roomId)
                ? roomId
                : null;
        }
    }

    private void EnsureCanEnterOffice(string connectionId)
    {
        if (!_playerRooms.TryGetValue(connectionId, out var currentRoomId))
        {
            return;
        }

        if (currentRoomId != SharedLobbyId)
        {
            throw new InvalidOperationException(
                "Player must leave the current Office Room first."
            );
        }

        var lobby = _rooms[SharedLobbyId];

        lobby.Players.Remove(connectionId);
        _playerRooms.Remove(connectionId);
    }

    private static PlayerInfo CreatePlayer(
        string connectionId,
        string displayName,
        PlayerRole? role)
    {
        return new PlayerInfo(
            connectionId,
            displayName,
            role,
            DateTime.UtcNow,
            0,
            0,
            0,
            0
        );
    }

    private static RoomSnapshot CreateSnapshot(GameRoom room)
    {
        return new RoomSnapshot(
            room.RoomId,
            room.Name,
            room.Type,
            room.IsPublic,
            room.Players.Count,
            room.Players.Values.ToArray()
        );
    }

    private sealed class GameRoom
    {
        public string RoomId { get; }
        public string Name { get; }
        public RoomType Type { get; }
        public bool IsPublic { get; }

        public Dictionary<string, PlayerInfo> Players { get; } = new();

        public GameRoom(
            string roomId,
            string name,
            RoomType type,
            bool isPublic)
        {
            RoomId = roomId;
            Name = name;
            Type = type;
            IsPublic = isPublic;
        }
    }

    public PlayerInfo? GetPlayer(string connectionId)
    {
        lock (_lock)
        {
            if (!_playerRooms.TryGetValue(connectionId, out var roomId) ||
                !_rooms.TryGetValue(roomId, out var room))
            {
                return null;
            }

            return room.Players.GetValueOrDefault(connectionId);
        }
    }

    public IReadOnlyCollection<PlayerInfo> GetRoomPlayers(string roomId)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(roomId, out var room)
                ? room.Players.Values.ToArray()
                : [];
        }
    }
}
