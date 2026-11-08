using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

public sealed record CreateRoomRequest(string OwnerSubject, string OwnerName, string Name);
public sealed record AddDeviceRequest(string OwnerSubject, string DeviceId, string DeviceName, string? PcName, string? ThumbnailBase64);
public sealed record DeviceResponse(string DeviceId, string DeviceName, string? PcName, string? ThumbnailBase64, bool Online);
public sealed record RoomResponse(string RoomId, string OwnerSubject, string OwnerName, string Name, bool IsActive, DeviceResponse[] Devices);

[ApiController]
[Route("api/[controller]")]
public sealed class RoomsController : ControllerBase
{
    private static readonly string StatePath = Path.Combine(AppContext.BaseDirectory, "rooms.json");
    private static readonly Dictionary<string, RoomResponse> Rooms = Load();

    private static Dictionary<string, RoomResponse> Load()
    {
        try { return System.IO.File.Exists(StatePath) ? JsonSerializer.Deserialize<Dictionary<string, RoomResponse>>(System.IO.File.ReadAllText(StatePath)) ?? new(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase); }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static void Save() => System.IO.File.WriteAllText(StatePath, JsonSerializer.Serialize(Rooms));

    [HttpPost]
    public ActionResult<RoomResponse> Create([FromBody] CreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerSubject)) return BadRequest(new { error = "OwnerSubject is required" });
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Room name is required" });
        string roomId;
        do { roomId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(); } while (Rooms.ContainsKey(roomId));
        var room = new RoomResponse(roomId, request.OwnerSubject, request.OwnerName, request.Name.Trim(), true, Array.Empty<DeviceResponse>());
        Rooms[roomId] = room; Save(); return Ok(room);
    }

    [HttpGet("me/{ownerSubject}")]
    public ActionResult<IEnumerable<RoomResponse>> Me(string ownerSubject) => Ok(Rooms.Values.Where(room => room.OwnerSubject == ownerSubject));

    [HttpGet("{roomId}")]
    public ActionResult<RoomResponse> Get(string roomId)
    {
        roomId = roomId.Trim().ToUpperInvariant(); return Rooms.TryGetValue(roomId, out var room) ? Ok(room) : NotFound(new { error = "Room not found" });
    }

    [HttpPost("{roomId}/devices")]
    public ActionResult<RoomResponse> AddDevice(string roomId, [FromBody] AddDeviceRequest request)
    {
        roomId = roomId.Trim().ToUpperInvariant();
        if (!Rooms.TryGetValue(roomId, out var room)) return NotFound(new { error = "Room not found" });
        if (!string.Equals(room.OwnerSubject, request.OwnerSubject, StringComparison.Ordinal)) return Forbid();
        var devices = room.Devices.Where(x => x.DeviceId != request.DeviceId).Append(new DeviceResponse(request.DeviceId, request.DeviceName, request.PcName, request.ThumbnailBase64, true)).ToArray();
        var updated = room with { Devices = devices }; Rooms[roomId] = updated; Save(); return Ok(updated);
    }

    [HttpDelete("{roomId}/devices/{deviceId}")]
    public ActionResult<RoomResponse> RemoveDevice(string roomId, string deviceId, [FromQuery] string ownerSubject)
    {
        roomId = roomId.Trim().ToUpperInvariant(); if (!Rooms.TryGetValue(roomId, out var room)) return NotFound();
        if (!string.Equals(room.OwnerSubject, ownerSubject, StringComparison.Ordinal)) return Forbid();
        var updated = room with { Devices = room.Devices.Where(x => x.DeviceId != deviceId).ToArray() }; Rooms[roomId] = updated; Save(); return Ok(updated);
    }
}
