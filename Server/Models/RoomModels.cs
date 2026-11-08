namespace Server.Models;

public sealed record RoomResponse(string RoomId, string OwnerSubject, bool IsActive);
public sealed record CreateRoomRequest(string OwnerSubject, string OwnerName);
public sealed record DeviceResponse(string DeviceId, bool Online);
