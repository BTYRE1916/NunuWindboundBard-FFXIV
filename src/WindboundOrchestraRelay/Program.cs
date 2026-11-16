using System.Threading.RateLimiting;
using Windbound.OrchestraRelay;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("HostRoom", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
    KeepAliveTimeout = TimeSpan.FromSeconds(15)
});
app.UseRateLimiter();

var relay = new RelayRooms();
app.MapGet("/healthz", () => Results.Text("ok"));
app.MapGet(RelayProtocol.HostPath, relay.HostAsync).RequireRateLimiting("HostRoom");
app.MapGet(RelayProtocol.JoinPath, relay.JoinAsync);
app.MapGet(RelayProtocol.AttachPath, relay.AttachAsync);
app.Run();
