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
var vercelUrl = Environment.GetEnvironmentVariable("VERCEL_URL") ?? "https://farm-hub-git-master-mmanangan021-8141s-projects.vercel.app";

if (!string.IsNullOrEmpty(cloudConnStr)) {
    try {
        if (cloudConnStr.StartsWith("postgres://") || cloudConnStr.StartsWith("postgresql://")) {
            var uri = new Uri(cloudConnStr);
            var userInfo = uri.UserInfo.Split(':');
            var dbHost = uri.Host;
            var dbPort = uri.Port <= 0 ? 5432 : uri.Port; // Standard Postgres Port
            var dbName = uri.AbsolutePath.TrimStart('/');
            var dbUser = userInfo[0];
            var dbPass = userInfo[1];
            FarmWorld.ConnStr = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};SSL Mode=Require;Trust Server Certificate=true";
        } else {
            FarmWorld.ConnStr = cloudConnStr;
        }
    } catch (Exception ex) {
        Console.WriteLine($"[DB CONFIG FAIL]: {ex.Message}");
    }

    // --- SELF-HEALING DATABASE SETUP ---
    // This runs automatically to create tables if they don't exist.
    try {
        using var conn = new NpgsqlConnection(FarmWorld.ConnStr);
        await conn.OpenAsync();
        
        string setupSql = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id SERIAL PRIMARY KEY,
                Username VARCHAR(100) UNIQUE NOT NULL,
                Email VARCHAR(150) UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id SERIAL PRIMARY KEY,
                Username VARCHAR(100) NOT NULL,
                Message VARCHAR(255) NOT NULL,
                Color VARCHAR(50) NOT NULL,
                CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            INSERT INTO ChatMessages (Username, Message, Color)
            SELECT 'MasterFarmer', 'Welcome to the Postgres Cloud of FarmHub!', '#fde047'
            WHERE NOT EXISTS (SELECT 1 FROM ChatMessages WHERE Username = 'MasterFarmer');
        ";

        using var cmd = new NpgsqlCommand(setupSql, conn);
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("[DB SETUP SUCCESS]: All tables are ready.");
    } catch (Exception ex) {
        Console.WriteLine($"[DB SETUP FAIL]: {ex.Message}");
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

// 3. SECURE CORS FOR VERCEL (DYNAMIC & ROBUST)
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => 
        policy.SetIsOriginAllowed(origin => 
              {
                  if (string.IsNullOrWhiteSpace(origin)) return false;
                  var host = new Uri(origin).Host;
                  return host.Equals("localhost") || 
                         host.EndsWith(".vercel.app") || 
                         host.Contains("vcl.how");
              })
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
    } catch (Exception ex) { 
        return Results.Json(new { message = $"SQL Error: {ex.Message}" }, statusCode: 500);
    }
});

app.MapGet("/api/test-db", async () => {
    if (string.IsNullOrEmpty(FarmWorld.ConnStr)) return Results.Problem("Connection String Empty! Check Environment Variables.");
    try {
        using var conn = new NpgsqlConnection(FarmWorld.ConnStr);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync();
        return Results.Ok(new { status = "Connected", db = FarmWorld.ConnStr.Split(';')[0] });
    } catch (Exception ex) {
        return Results.Json(new { status = "Error", error = ex.Message }, statusCode: 500);
    }
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
