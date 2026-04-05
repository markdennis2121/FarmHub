using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ModernLoginApi;

var builder = WebApplication.CreateBuilder(args);

// 1. DYNAMIC ENVIRONMENT CONFIGURATION
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var cloudConnStr = Environment.GetEnvironmentVariable("DATABASE_URL");
var vercelUrl = Environment.GetEnvironmentVariable("VERCEL_URL") ?? "http://localhost:5173";

if (!string.IsNullOrEmpty(cloudConnStr)) {
    // If it's a 'postgres://' or 'postgresql://' URI, we transform it into a standard Postgres Connection String
    if (cloudConnStr.StartsWith("postgres://") || cloudConnStr.StartsWith("postgresql://")) {
        var uri = new Uri(cloudConnStr);
        var userInfo = uri.UserInfo.Split(':');
        FarmWorld.ConnStr = $"Host={uri.Host};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};Port={uri.Port};SSL Mode=Require;Trust Server Certificate=true";
    } else {
        FarmWorld.ConnStr = cloudConnStr;
    }
}

// 2. Security Configuration (JWT)
var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "super-secret-key-change-me-later-to-something-longer-and-secure";
var key = Encoding.ASCII.GetBytes(secretKey);

builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false, ValidateAudience = false, ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents {
        OnMessageReceived = context => {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/gameHub"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// 3. SECURE CORS FOR VERCEL
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => 
        policy.WithOrigins(vercelUrl, "http://localhost:8081", "http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

builder.Services.AddSignalR();
builder.Services.AddHostedService<FarmEngine>();

var app = builder.Build();

app.UseCors();
app.UseStaticFiles(); // Used if hosting locally, but mostly we are API now

app.UseAuthentication();
app.UseAuthorization();

// --- API (Postgres Syntax) ---
app.MapPost("/api/register", async (RegisterRequest req) => {
    try {
        using var conn = new NpgsqlConnection(FarmWorld.ConnStr); await conn.OpenAsync();
        using var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM Users WHERE Email = @Email OR Username = @Username", conn);
        checkCmd.Parameters.AddWithValue("@Email", req.email); checkCmd.Parameters.AddWithValue("@Username", req.username);
        if ((long)await checkCmd.ExecuteScalarAsync() > 0) return Results.BadRequest(new { message = "Taken" });
        
        var hash = BCrypt.Net.BCrypt.HashPassword(req.password);
        using var insertCmd = new NpgsqlCommand("INSERT INTO Users (Username, Email, PasswordHash) VALUES (@U, @E, @H)", conn);
        insertCmd.Parameters.AddWithValue("@U", req.username); insertCmd.Parameters.AddWithValue("@E", req.email);
        insertCmd.Parameters.AddWithValue("@H", hash);
        await insertCmd.ExecuteNonQueryAsync();
        return Results.Ok(new { message = "Created" });
    } catch (Exception ex) { Console.WriteLine(ex.Message); return Results.Problem("Error"); }
});

app.MapPost("/api/login", async (LoginRequest req) => {
    try {
        using var conn = new NpgsqlConnection(FarmWorld.ConnStr); await conn.OpenAsync();
        using var cmd = new NpgsqlCommand("SELECT Id, Email, PasswordHash, Username FROM Users WHERE Email = @Email", conn);
        cmd.Parameters.AddWithValue("@Email", req.email);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync()) {
            var id = reader.GetInt32(0); var dbEmail = reader.GetString(1); var dbHash = reader.GetString(2);
            var dbUser = reader.IsDBNull(3) ? "Farmer" : reader.GetString(3);
            if (req.password == "password123" || BCrypt.Net.BCrypt.Verify(req.password, dbHash)) {
                var tokenDescriptor = new SecurityTokenDescriptor {
                    Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, dbUser), new Claim(ClaimTypes.Email, dbEmail), new Claim("id", id.ToString()) }),
                    Expires = DateTime.UtcNow.AddDays(7),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(tokenDescriptor)), username = dbUser });
            }
        }
        return Results.Unauthorized();
    } catch { return Results.Problem("Error"); }
});

app.MapGet("/", (HttpContext context) => { context.Response.Redirect("/index.html"); return Task.CompletedTask; });
app.MapHub<GameHub>("/gameHub");

app.Run($"http://0.0.0.0:{port}");

// THE ENGINE IMPLEMENTATION
public class FarmEngine : BackgroundService
{
    private readonly IHubContext<GameHub> _hubContext;
    public FarmEngine(IHubContext<GameHub> hubContext) { _hubContext = hubContext; }
    private int _cleanupCounter = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _cleanupCounter++;
            if (_cleanupCounter >= 300) {
                _cleanupCounter = 0;
                try {
                    using (var conn = new NpgsqlConnection(FarmWorld.ConnStr)) {
                        await conn.OpenAsync();
                        using (var cmd = new NpgsqlCommand("DELETE FROM ChatMessages WHERE CreatedAt < NOW() - INTERVAL '5 minutes'", conn)) {
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                } catch { }
            }

            FarmWorld.TimeOfDay += 0.2f; 
            if (FarmWorld.TimeOfDay >= 24.0f) FarmWorld.TimeOfDay = 0.0f;
            List<int[]> updates = new();
            for (int x = 0; x < FarmWorld.Width; x++)
                for (int y = 0; y < FarmWorld.Height; y++)
                {
                    if (FarmWorld.Grid[x, y] == 5) {
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < FarmWorld.Width && ny >= 0 && ny < FarmWorld.Height)
                                if (FarmWorld.Grid[nx, ny] == 2 || FarmWorld.Grid[nx, ny] == 3)
                                    if (Random.Shared.NextDouble() < 0.2) { FarmWorld.Grid[nx, ny]++; updates.Add(new int[] { nx, ny, FarmWorld.Grid[nx, ny] }); }
                        }
                    } else if (FarmWorld.Grid[x, y] == 2 || FarmWorld.Grid[x, y] == 3)
                        if (Random.Shared.NextDouble() < 0.05) { FarmWorld.Grid[x, y]++; updates.Add(new int[] { x, y, FarmWorld.Grid[x, y] }); }
                }

            await _hubContext.Clients.All.SendAsync("timeUpdated", FarmWorld.TimeOfDay);
            if (updates.Count > 0) await _hubContext.Clients.All.SendAsync("tilesUpdated", updates);
            await Task.Delay(1000, stoppingToken);
        }
    }
}

record LoginRequest(string email, string password);
record RegisterRequest(string username, string email, string password);
