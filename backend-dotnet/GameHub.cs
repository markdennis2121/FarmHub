using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace ModernLoginApi
{
    public class PlayerState
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public int Coins { get; set; }
    }

    public class FarmWorld
    {
        public const int Width = 60;
        public const int Height = 40;
        public static byte[,] Grid = new byte[Width, Height]; 
        public static float TimeOfDay = 6.0f; // Start at 6 AM
        // Default Local/Dev string - overridden by env var in Program.cs
        public static string ConnStr = "Host=localhost;Database=ModernLoginDB;Username=postgres;Password=password";
    }

    // THE FARM HUB: CLEAN SOCIAL LAYER
    [Authorize]
    public class GameHub : Hub
    {
        public static ConcurrentDictionary<string, PlayerState> _players = new();

        public override async Task OnConnectedAsync()
        {
            var username = Context.User?.Identity?.Name ?? "AnonymousFarmer";
            var colors = new[] { "#ff4500", "#1e90ff", "#8a2be2", "#ff1493", "#f8f8f2", "#00ced1" };
            
            var newPlayer = new PlayerState {
                Id = Context.ConnectionId,
                Username = username,
                Color = colors[Random.Shared.Next(colors.Length)],
                X = 300, Y = 300, Coins = 100 
            };
            _players.TryAdd(Context.ConnectionId, newPlayer);

            var flatGrid = new List<int>();
            for(int y = 0; y < FarmWorld.Height; y++)
                for(int x = 0; x < FarmWorld.Width; x++)
                    flatGrid.Add(FarmWorld.Grid[x,y]);

            await Clients.Caller.SendAsync("initFarm", _players.Values, FarmWorld.Width, FarmWorld.Height, flatGrid, FarmWorld.TimeOfDay, newPlayer.Coins);
            
            // --- LOAD CHAT HISTORY (Postgres Syntax) ---
            var history = new List<object>();
            try {
                using (var conn = new NpgsqlConnection(FarmWorld.ConnStr)) {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand("SELECT Username, Message, Color FROM ChatMessages ORDER BY CreatedAt DESC LIMIT 30", conn)) {
                        using (var reader = await cmd.ExecuteReaderAsync()) {
                            while (await reader.ReadAsync())
                                history.Add(new { username = reader.GetString(0), message = reader.GetString(1), color = reader.GetString(2) });
                        }
                    }
                }
                history.Reverse();
                await Clients.Caller.SendAsync("chatHistory", history);
            } catch (Exception ex) { Console.WriteLine($"[HISTORY LOAD FAIL]: {ex.Message}"); }

            await Clients.Others.SendAsync("playerJoined", newPlayer);
            await base.OnConnectedAsync();
        }

        public async Task UpdatePosition(float x, float y)
        {
            if (_players.TryGetValue(Context.ConnectionId, out var player)) {
                player.X = x; player.Y = y;
                await Clients.Others.SendAsync("playerMoved", player.Id, x, y);
            }
        }

        public async Task Interact(int tx, int ty)
        {
            if (tx < 0 || tx >= FarmWorld.Width || ty < 0 || ty >= FarmWorld.Height) return;
            byte currentState = FarmWorld.Grid[tx, ty];
            byte newState = currentState;
            if (currentState == 0) newState = 1; 
            else if (currentState == 1) newState = 2; 
            else if (currentState == 4) {
                newState = 1; 
                if (_players.TryGetValue(Context.ConnectionId, out var player)) {
                    player.Coins += 15;
                    await Clients.Caller.SendAsync("coinsUpdated", player.Coins);
                }
            }
            if (newState != currentState) {
                FarmWorld.Grid[tx, ty] = newState;
                await Clients.All.SendAsync("tilesUpdated", new List<int[]> { new int[] { tx, ty, newState } });
                await Clients.All.SendAsync("playSound", "interact", currentState); 
            }
        }

        public async Task BuySprinkler(int tx, int ty)
        {
            if (tx < 0 || tx >= FarmWorld.Width || ty < 0 || ty >= FarmWorld.Height) return;
            if (FarmWorld.Grid[tx, ty] != 0 && FarmWorld.Grid[tx, ty] != 1) return;
            if (_players.TryGetValue(Context.ConnectionId, out var player) && player.Coins >= 50) {
                player.Coins -= 50;
                FarmWorld.Grid[tx, ty] = 5; 
                await Clients.Caller.SendAsync("coinsUpdated", player.Coins);
                await Clients.All.SendAsync("tilesUpdated", new List<int[]> { new int[] { tx, ty, 5 } });
            }
        }

        public async Task SendChat(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (message.Length > 80) message = message.Substring(0, 80);
            if (_players.TryGetValue(Context.ConnectionId, out var player)) {
                try {
                    using (var conn = new NpgsqlConnection(FarmWorld.ConnStr)) {
                        await conn.OpenAsync();
                        using (var cmd = new NpgsqlCommand("INSERT INTO ChatMessages (Username, Message, Color) VALUES (@U, @M, @C)", conn)) {
                            cmd.Parameters.AddWithValue("@U", player.Username);
                            cmd.Parameters.AddWithValue("@M", message);
                            cmd.Parameters.AddWithValue("@C", player.Color);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                } catch (Exception ex) { Console.WriteLine($"[SAVE CHAT FAIL]: {ex.Message}"); }
                await Clients.All.SendAsync("receiveChat", player.Color, player.Username, message);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _players.TryRemove(Context.ConnectionId, out _);
            await Clients.All.SendAsync("playerLeft", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
