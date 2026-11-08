using Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 20 * 1024 * 1024;
});

var webOrigins = (Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(webOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();
app.MapHub<RoomHub>("/hubs/room");
app.MapGet("/downloads/RemoteMumu-Agent.zip", () =>
{
    var filePath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "publish", "RemoteMumu-Agent.zip"));
    return File.Exists(filePath)
        ? Results.File(filePath, "application/zip", "RemoteMumu-Agent.zip")
        : Results.NotFound(new { error = "Connector package not found" });
});

app.Run();
