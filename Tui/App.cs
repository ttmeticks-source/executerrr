using Bozo.Core;

namespace Bozo.Tui;

public sealed class App
{
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bozo", "config.env");

    public async Task RunAsync()
    {
        Console.Title = "Bozo Key Approval";
        Console.WriteLine("BOZO KEY APPROVAL");
        Console.WriteLine("-----------------");
        Console.WriteLine("Enter your key to request access. Press Ctrl+C to quit.");
        Console.WriteLine();

        var config = ReadConfig(_configPath);
        var token = config.GetValueOrDefault("DISCORD_BOT_TOKEN") ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        var channelText = config.GetValueOrDefault("DISCORD_CHANNEL_ID") ?? Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");

        if (string.IsNullOrWhiteSpace(token) || !ulong.TryParse(channelText, out var channelId))
        {
            Console.WriteLine("Discord setup is missing.");
            Console.WriteLine($"Create this config file: {_configPath}");
            Console.WriteLine("Add these two lines:");
            Console.WriteLine("DISCORD_BOT_TOKEN=your_bot_token");
            Console.WriteLine("DISCORD_CHANNEL_ID=your_channel_id");
            Console.WriteLine();
            Console.WriteLine("A webhook URL alone cannot receive Allow/Decline button clicks; this app needs a Discord bot.");
            return;
        }

        Console.Write("Key: ");
        var key = Console.ReadLine() ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("No key entered.");
            return;
        }

        try
        {
            await using var bot = new ApprovalBot(token, channelId);
            Console.WriteLine("Connecting to Discord...");
            await bot.StartAsync();
            Console.WriteLine("Request sent. Waiting up to 10 minutes for a decision...");
            var allowed = await bot.RequestApprovalAsync(key);

            Console.Clear();
            if (allowed)
            {
                Console.WriteLine("┌──────────────────────────────┐");
                Console.WriteLine("│                              │");
                Console.WriteLine("│        ACCESS ALLOWED        │");
                Console.WriteLine("│                              │");
                Console.WriteLine("└──────────────────────────────┘");
            }
            else
            {
                Console.WriteLine("Lol u thought u are gonna get in Skid haha.");
            }
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Approval request expired.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Discord error: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ReadConfig(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var split = trimmed.Split('=', 2);
            if (split.Length == 2) values[split[0].Trim()] = split[1].Trim();
        }
        return values;
    }
}
