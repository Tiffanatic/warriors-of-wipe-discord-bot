using Discord;
using Discord.WebSocket;
using WarriorsOfWipeBot;

public static class Program
{
    public static async Task Main()
    {
        var config = new Json<Config>("config.json");
        if (string.IsNullOrWhiteSpace(config.Data.token))
        {
            Console.WriteLine("Token not set");
            return;
        }

        // MessageContent used for NoLfg.cs
        DiscordSocketClient client = new(new()
            { GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent });
        client.Log += m =>
        {
            Console.WriteLine(m);
            return Task.CompletedTask;
        };
        _ = new Raid(client);
        _ = new Omegapoll(client);
        _ = new GetPollResults(client);
        _ = new NoLfg(client);
        client.Ready += () =>
            client.BulkOverwriteGlobalApplicationCommandsAsync(Raid.Commands.Concat(Omegapoll.Commands)
                .Concat(GetPollResults.Commands).ToArray());
        await client.LoginAsync(TokenType.Bot, config.Data.token);
        await client.StartAsync();
        await Task.Delay(-1);
    }
}

[Serializable]
internal class Config
{
    public string token = "";
}
