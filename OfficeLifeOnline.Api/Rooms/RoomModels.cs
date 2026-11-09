
namespace OfficeLifeOnline.Api.Rooms;

public sealed record PlayerInfo(
    string ConnectionId,
    string DisplayName,
    PlayerRole? Role,
    DateTime JoinedAt,
    float X,
    float Y,
    float Z,
    float RotationY
);

public sealed record RoomSnapshot(
    string RoomId,
    string Name,
    RoomType Type,
    bool IsPublic,
    int PlayerCount,
    IReadOnlyCollection<PlayerInfo> Players
);

public sealed record RoomLeaveResult(
    string RoomId,
    RoomType RoomType,
    string DisplayName,
    bool RoomClosed,
    IReadOnlyCollection<PlayerInfo> RemainingPlayers
);

public sealed record RoomJoinResult(
    RoomSnapshot Room,
    PlayerInfo Player
);

public sealed record PlayerPositionUpdate(
    string ConnectionId,
    float X,
    float Y,
    float Z,
    float RotationY
);
