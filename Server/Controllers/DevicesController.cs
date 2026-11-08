using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

public sealed record RegisterDeviceRequest(string GoogleSubject, string PcName, string DeviceId, string DeviceName);
public sealed record SyncDevicesRequest(string GoogleSubject, string PcName, string[] DeviceIds, string[] DeviceNames);
public sealed record PcDeviceResponse(string PcName, string DeviceId, string DeviceName, bool Online);

[ApiController]
[Route("api/[controller]")]
public sealed class DevicesController : ControllerBase
{
    private static readonly string StatePath = Path.Combine(AppContext.BaseDirectory, "devices.json");
    private static readonly Dictionary<string, PcDeviceResponse> Devices = Load();

    private static Dictionary<string, PcDeviceResponse> Load()
    {
        try
        {
            if (!System.IO.File.Exists(StatePath)) return new(StringComparer.OrdinalIgnoreCase);
            var data = JsonSerializer.Deserialize<Dictionary<string, PcDeviceResponse>>(System.IO.File.ReadAllText(StatePath));
            return new Dictionary<string, PcDeviceResponse>(data ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static void Save()
    {
        System.IO.File.WriteAllText(StatePath, JsonSerializer.Serialize(Devices));
    }

    private static void RemovePcDevices(string googleSubject, string pcName)
    {
        foreach (var key in Devices.Keys.Where(key => key.StartsWith(googleSubject + ":", StringComparison.OrdinalIgnoreCase) && key.Contains($":{pcName}:", StringComparison.OrdinalIgnoreCase)).ToArray())
            Devices.Remove(key);
    }

    [HttpPost("sync")]
    public ActionResult<IEnumerable<PcDeviceResponse>> Sync([FromBody] SyncDevicesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleSubject) || string.IsNullOrWhiteSpace(request.PcName))
            return BadRequest(new { error = "GoogleSubject and PcName are required" });

        RemovePcDevices(request.GoogleSubject, request.PcName);
        var deviceIds = request.DeviceIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < deviceIds.Length; i++)
        {
            var deviceId = deviceIds[i];
            var deviceName = request.DeviceNames != null && i < request.DeviceNames.Length && !string.IsNullOrWhiteSpace(request.DeviceNames[i])
                ? request.DeviceNames[i] : $"MuMu {deviceId}";
            var key = $"{request.GoogleSubject}:{request.PcName}:{deviceId}";
            Devices[key] = new PcDeviceResponse(request.PcName, deviceId, deviceName, true);
        }
        Save();

        var prefix = request.GoogleSubject + ":";
        return Ok(Devices.Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value));
    }

    [HttpPost("register")]
    public ActionResult<PcDeviceResponse> Register([FromBody] RegisterDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleSubject) || string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest(new { error = "GoogleSubject and DeviceId are required" });
        var key = $"{request.GoogleSubject}:{request.PcName}:{request.DeviceId}";
        Devices[key] = new PcDeviceResponse(request.PcName, request.DeviceId, request.DeviceName, true);
        Save();
        return Ok(Devices[key]);
    }

    [HttpGet("me/{googleSubject}")]
    public ActionResult<IEnumerable<PcDeviceResponse>> Me(string googleSubject)
    {
        var prefix = googleSubject + ":";
        return Ok(Devices.Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value));
    }

    [HttpDelete("pc/{googleSubject}/{pcName}")]
    public IActionResult RemovePc(string googleSubject, string pcName)
    {
        RemovePcDevices(googleSubject, pcName);
        Save();
        return NoContent();
    }
}
