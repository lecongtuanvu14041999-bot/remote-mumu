using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

ApplicationConfiguration.Initialize();
Application.Run(new ConnectorForm());

sealed class ConnectorForm : Form
{
    private readonly Button googleButton = new() { Text = "Đăng nhập Google", Location = new Point(170, 24), Width = 300, Height = 42, BackColor = Color.FromArgb(91,77,245), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button logoutButton = new() { Text = "Đăng xuất", Location = new Point(170, 72), Width = 300, Height = 36, Enabled = false };
    private readonly Button scanButton = new() { Text = "Quét lại MumuPlayer", Location = new Point(170, 124), Width = 300, Height = 42, Enabled = false };
    private readonly ListView devicesList = new() { Location = new Point(170, 178), Width = 300, Height = 230, View = View.Details, FullRowSelect = true };
    private readonly Label pcLabel = new() { Text = "Tên PC", Location = new Point(170, 418), Width = 80, Height = 24 };
    private readonly TextBox pcNameBox = new() { Text = Environment.MachineName, Location = new Point(250, 414), Width = 220, Height = 28 };
    private readonly Label status = new() { Text = "Chưa kết nối Google", Location = new Point(170, 450), Width = 300, Height = 44, TextAlign = ContentAlignment.MiddleCenter };
    private readonly HttpListener listener = new();
    private readonly HttpClient api = new();
    private readonly string callbackUrl;
    private readonly System.Windows.Forms.Timer scanTimer = new() { Interval = 5000 };
    private readonly string statePath;
    private readonly string settingsPath;
    private string? googleSubject;
    private string? googleName;
    private string? adbPath;
    private readonly string apiUrl = Environment.GetEnvironmentVariable("REMOTEMUMU_API_URL")?.TrimEnd('/') ?? "https://remote-mumu.onrender.com";
    private readonly string webUrl = Environment.GetEnvironmentVariable("REMOTEMUMU_WEB_URL")?.TrimEnd('/') ?? "https://lecongtuanvu14041999-bot.github.io/remote-mumu";

    public ConnectorForm()
    {
        Text = "Remote Mumu Connector";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245,247,251);
        devicesList.Columns.Add("MumuPlayer", 220);
        devicesList.Columns.Add("Trạng thái", 70);
        Controls.AddRange([googleButton, logoutButton, scanButton, devicesList, pcLabel, pcNameBox, status]);
        callbackUrl = StartCallbackListener();
        googleButton.Click += (_, _) => OpenLogin();
        logoutButton.Click += (_, _) => Logout();
        scanButton.Click += (_, _) => _ = ScanAndSyncAsync();
        pcLabel.Text = $"PC: {Environment.MachineName}";
        adbPath = FindAdb();
        statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemoteMumu", "connector.json");
        settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemoteMumu", "settings.json");
        LoadSettings();
        LoadSavedProfile();
        pcNameBox.TextChanged += (_, _) => SaveSettings();
        scanTimer.Tick += (_, _) => { if (googleSubject != null) _ = ScanAndSyncAsync(); };
        scanTimer.Start();
    }

    private string StartCallbackListener()
    {
        const string url = "http://127.0.0.1:43189/connector/";
        listener.Prefixes.Add(url);
        listener.Start();
        _ = ListenAsync();
        return url;
    }

    private async Task ListenAsync()
    {
        while (listener.IsListening)
        {
            var context = await listener.GetContextAsync();
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            context.Response.ContentType = "text/plain";
            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                continue;
            }
            if (context.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var profile = JsonSerializer.Deserialize<GoogleProfile>(await reader.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (profile != null)
                {
                    googleSubject = profile.Subject;
                    googleName = profile.Name;
                    SaveProfile(profile);
                    BeginInvoke(async () =>
                    {
                        googleButton.Text = $"Google: {googleName}";
                        googleButton.Enabled = false;
                        logoutButton.Enabled = true;
                        scanButton.Enabled = true;
                        status.Text = $"Đã kết nối Google: {profile.Email} - đang quét...";
                        await ScanAndSyncAsync();
                    });
                }
            }
            var bytes = Encoding.UTF8.GetBytes("OK");
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }
    }

    private void OpenLogin() => Process.Start(new ProcessStartInfo($"{webUrl}/?connector={Uri.EscapeDataString(callbackUrl)}") { UseShellExecute = true });

    private void Logout()
    {
        var subjectToRemove = googleSubject;
        googleSubject = null;
        googleName = null;
        devicesList.Items.Clear();
        if (File.Exists(statePath)) File.Delete(statePath);
        googleButton.Text = "Đăng nhập Google";
        googleButton.Enabled = true;
        logoutButton.Enabled = false;
        scanButton.Enabled = false;
        status.Text = "Đã đăng xuất tài khoản";
        if (!string.IsNullOrWhiteSpace(subjectToRemove))
            _ = api.DeleteAsync($"{apiUrl}/api/devices/pc/{Uri.EscapeDataString(subjectToRemove)}/{Uri.EscapeDataString(PcDisplayName)}");
    }

    private string? FindAdb()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var candidates = new[]
            {
                Path.Combine(root, "Netease", "MuMuPlayer", "nx_main", "adb.exe"),
                Path.Combine(root, "Netease", "MuMuPlayer", "nx_device", "12.0", "shell", "adb.exe"),
                Path.Combine(root, "Netease", "MuMuPlayerGlobal", "nx_main", "adb.exe"),
                Path.Combine(root, "Netease", "MuMuPlayerGlobal-12.0", "shell", "adb.exe"),
                Path.Combine(root, "Netease", "MuMuPlayer-12.0", "shell", "adb.exe")
            };
            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private async Task ScanAndSyncAsync()
    {
        if (googleSubject == null) return;
        devicesList.Items.Clear();
        if (adbPath == null) { status.Text = "Không tìm thấy ADB MuMuPlayer"; return; }
        var output = RunAdb("devices");
        var ids = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(x => x.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(x => x?.StartsWith("emulator-") == true)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var id in ids)
        {
            devicesList.Items.Add(new ListViewItem([id!, "Online"]));
        }
        await SyncDevicesAsync(ids);
        status.Text = ids.Count == 0 ? "Không tìm thấy emulator đang chạy" : $"Đã đồng bộ {ids.Count} emulator";
    }

    private async Task SyncDevicesAsync(List<string> deviceIds)
    {
        var body = JsonSerializer.Serialize(new { GoogleSubject = googleSubject, PcName = PcDisplayName, DeviceIds = deviceIds, DeviceNames = deviceIds.Select(GetMumuName) });
        await api.PostAsync($"{apiUrl}/api/devices/sync", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private string RunAdb(string arguments)
    {
        if (adbPath == null) return "";
        using var process = Process.Start(new ProcessStartInfo { FileName = adbPath, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private void LoadState()
    {
        if (!File.Exists(statePath)) return;
        try
        {
            using var stream = File.OpenRead(statePath);
            using var reader = new StreamReader(stream);
            googleButton.Text = reader.ReadLine() ?? "Đăng nhập Google";
            googleButton.Enabled = reader.ReadLine() != "1";
            logoutButton.Enabled = reader.ReadLine() == "1";
            scanButton.Enabled = reader.ReadLine() == "1";
            pcLabel.Text = $"PC: {reader.ReadLine()}";
            status.Text = reader.ReadLine() ?? "Chưa kết nối Google";
            if (googleButton.Enabled)
            {
                googleSubject = null;
                googleName = null;
            }
            else
            {
                googleSubject = reader.ReadLine();
                googleName = reader.ReadLine();
            }
        }
        catch { }
    }

    private void SaveState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        using var stream = File.OpenWrite(statePath);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(googleButton.Text);
        writer.WriteLine(googleButton.Enabled ? "0" : "1");
        writer.WriteLine(logoutButton.Enabled ? "1" : "0");
        writer.WriteLine(scanButton.Enabled ? "1" : "0");
        writer.WriteLine(pcLabel.Text);
        writer.WriteLine(status.Text);
        writer.WriteLine(googleSubject);
        writer.WriteLine(googleName);
    }

    private void LoadSavedProfile()
    {
        try
        {
            if (!File.Exists(statePath)) return;
            var profile = JsonSerializer.Deserialize<GoogleProfile>(File.ReadAllText(statePath));
            if (profile == null) return;
            googleSubject = profile.Subject;
            googleName = profile.Name;
            googleButton.Text = $"Google: {googleName}";
            googleButton.Enabled = false;
            logoutButton.Enabled = true;
            scanButton.Enabled = true;
            status.Text = "Đã kết nối Google - đang quét MumuPlayer...";
            _ = ScanAndSyncAsync();
        }
        catch { }
    }

    private void SaveProfile(GoogleProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(profile));
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath));
            if (settings == null) return;
            pcNameBox.Text = settings.PcDisplayName;
        }
        catch { }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        using var stream = File.OpenWrite(settingsPath);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(pcNameBox.Text.Trim());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    private string PcDisplayName { get { return string.IsNullOrWhiteSpace(pcNameBox.Text) ? Environment.MachineName : pcNameBox.Text.Trim(); } }

    private string GetMumuName(string serial)
    {
        var manager = @"C:\Program Files\Netease\MuMuPlayer\nx_main\MuMuManager.exe";
        if (!File.Exists(manager) || !serial.StartsWith("emulator-")) return $"MuMu {serial}";
        var portText = serial.Replace("emulator-", "");
        if (!int.TryParse(portText, out var port)) return $"MuMu {serial}";
        var index = (port - 5554) / 2;
        if (index < 0 || index > 31) return $"MuMu {serial}";
        using var process = Process.Start(new ProcessStartInfo { FileName = manager, Arguments = $"info -v {index}", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
        var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString())) return name.GetString()!.Trim();
        }
        catch { }
        return $"MuMu {serial}";
    }
}

sealed record GoogleProfile(string Subject, string Name, string Email, string Picture);
sealed record Settings(string PcDisplayName);
