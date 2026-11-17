using Discord;
using Discord.WebSocket;

namespace Bozo.Core;

public sealed class ApprovalBot : IAsyncDisposable
{
    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds
    });
    private readonly ulong _channelId;
    private readonly string _token;
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pending = new();

    public ApprovalBot(string token, ulong channelId)
    {
        _token = token;
        _channelId = channelId;
        _client.ButtonExecuted += OnButtonAsync;
    }

    public async Task StartAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
        for (var i = 0; i < 30 && _client.ConnectionState != ConnectionState.Connected; i++)
            await Task.Delay(500);
        if (_client.ConnectionState != ConnectionState.Connected)
            throw new InvalidOperationException("Discord bot did not connect. Check its token and network.");
    }

    public async Task<bool> RequestApprovalAsync(string submittedKey)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var channel = _client.GetChannel(_channelId) as IMessageChannel;
        if (channel is null)
            throw new InvalidOperationException("Bot cannot see that channel. Check the channel ID and bot permissions.");

        var embed = new EmbedBuilder()
            .WithTitle("New key approval request")
            .WithDescription("A user submitted a key. Choose Allow or Decline.")
            .AddField("Key", submittedKey)
            .WithColor(Color.DarkGrey)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("Allow", $"allow:{id}", ButtonStyle.Success)
            .WithButton("Decline", $"decline:{id}", ButtonStyle.Danger)
            .Build();

        await channel.SendMessageAsync(embed: embed, components: components);
        try { return await completion.Task.WaitAsync(TimeSpan.FromMinutes(10)); }
        finally { _pending.Remove(id); }
    }

    private async Task OnButtonAsync(SocketMessageComponent component)
    {
        var parts = component.Data.CustomId.Split(':', 2);
        if (parts.Length != 2 || !_pending.TryGetValue(parts[1], out var completion))
        {
            await component.RespondAsync("This request has expired.", ephemeral: true);
            return;
        }

        var allowed = parts[0] == "allow";
        completion.TrySetResult(allowed);
        await component.UpdateAsync(message =>
        {
            message.Components = new ComponentBuilder().Build();
            message.Embed = new EmbedBuilder()
                .WithTitle(allowed ? "Request allowed" : "Request declined")
                .WithDescription(allowed ? "The key request was approved." : "The key request was declined.")
                .WithColor(allowed ? Color.Green : Color.Red)
                .Build();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _client.StopAsync();
        _client.Dispose();
    }
}
