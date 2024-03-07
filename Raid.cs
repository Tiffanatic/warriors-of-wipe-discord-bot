using Discord;
using Discord.WebSocket;
using System.Globalization;

namespace WarriorsOfWipeBot;

internal class Raid
{
    public static readonly SlashCommandProperties[] Commands =
    [
        new SlashCommandBuilder()
            .WithName("raidcreate")
            .WithDescription("Create a raid")
            .AddOption("raid", ApplicationCommandOptionType.String, "The name of the raid to create", isRequired: true)
            .AddOption("time", ApplicationCommandOptionType.String, "Time: yyyy-MM-dd hh:mm timezone (defaults to ST)", isRequired: true)
            .Build()
    ];

    private record struct Job(string Id, string Emote, string Name);

    private static readonly Job[] Jobs =
    [
        new("PLD", "PLD", "Paladin"),
        new("NIN", "NIN", "Ninja"),
        new("DNC", "DNC", "Dancer"),
    ];

    private const string sprout = "🌱";
    private const string crown = "👑";

    private readonly Json<Dictionary<ulong, RaidData>> Raids = new("raid.json");
    private readonly Json<Dictionary<ulong, string>> UserJobs = new("userjobs.json");
    [Serializable]
    private class RaidData(string title, long time, List<RaidDataMember> members)
    {
        public string Title = title;
        public long Time = time;
        public List<RaidDataMember> Members = members;
    }
    [Serializable]
    private class RaidDataMember(ulong userId, string job, bool helper, bool sprout, bool mentor)
    {
        public ulong UserId = userId;
        public string Job = job;
        public bool Helper = helper;
        public bool Sprout = sprout;
        public bool Mentor = mentor;
    }

    public Raid(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += SlashCommandExecuted;
        client.ButtonExecuted += ButtonExecuted;
        client.MessageDeleted += MessageDeleted;
    }

    private static MessageComponent BuildMessageComponents()
    {
        var components = new ComponentBuilder();
        ActionRowBuilder rowBuilder = new();
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("signup").WithStyle(ButtonStyle.Secondary).WithLabel("Sign up").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("helpout").WithStyle(ButtonStyle.Secondary).WithLabel("Available as helper").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("withdraw").WithStyle(ButtonStyle.Secondary).WithLabel("Withdraw").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("resetclass").WithStyle(ButtonStyle.Secondary).WithLabel("Reset class").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("ping").WithStyle(ButtonStyle.Secondary).WithLabel("Ping").Build());
        components.AddRow(rowBuilder);
        return components.Build();
    }

    private static Embed BuildEmbed(RaidData raidData)
    {
        EmbedBuilder embed = new()
        {
            Title = raidData.Title,
            Description = $"<t:{raidData.Time}:F>",
            Color = new Color(0xff1155)
        };
        static string FormatMember(RaidDataMember raidDataMember)
        {
            var job = raidDataMember.Job;
            foreach (var j in Jobs)
            {
                if (j.Id == raidDataMember.Job)
                {
                    job = j.Emote;
                    break;
                }
            }
            return $"{job} {(raidDataMember.Mentor ? crown : "")}{(raidDataMember.Sprout ? sprout : "")}{MentionUtils.MentionUser(raidDataMember.UserId)}";
        }

        var players = string.Join("\n", raidData.Members.Where(m => !m.Helper).Select(FormatMember));
        var helpers = string.Join("\n", raidData.Members.Where(m => m.Helper).Select(FormatMember));
        embed.AddField("Players", string.IsNullOrWhiteSpace(players) ? "---" : players, true);
        embed.AddField("Helpers", string.IsNullOrWhiteSpace(helpers) ? "---" : helpers, true);
        return embed.Build();
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName != "raidcreate")
            return;
        var options = command.Data.Options.ToList();
        if (options.Count != 2)
            return;
        var title = (string)options[0].Value;
        if (!DateTimeOffset.TryParse((string)options[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            await command.RespondAsync("Invalid time format", ephemeral: true);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (time < now)
        {
            await command.RespondAsync("Cannot make a raid in the past", ephemeral: true);
            return;
        }
        if (time > now.AddDays(7))
        {
            await command.RespondAsync("Cannot make a raid more than a week from now", ephemeral: true);
            return;
        }
        var timestamp = time.ToUnixTimeSeconds();
        var raidData = new RaidData(title, timestamp, []);
        var channel = await command.GetChannelAsync();
        var message = await channel.SendMessageAsync(allowedMentions: new AllowedMentions(AllowedMentionTypes.None), components: BuildMessageComponents(), embed: BuildEmbed(raidData));
        Raids.Data.Add(message.Id, raidData);
        await command.RespondAsync("Created", ephemeral: true);
    }

    private async Task ButtonExecuted(SocketMessageComponent component)
    {
        switch (component.Data.CustomId)
        {
            case "signup":
            case "helpout":
                if (Raids.Data.TryGetValue(component.Message.Id, out var raidData))
                {
                    if (UserJobs.Data.TryGetValue(component.User.Id, out var job))
                    {
                        raidData.Members.RemoveAll(m => m.UserId == component.User.Id);
                        bool isMentor = false, isSprout = false;
                        if (component.User is SocketGuildUser guildUser)
                        {
                            foreach (var role in guildUser.Roles)
                            {
                                isMentor = role.Id == 1208606814770565131UL;
                                isSprout = role.Id == 1208606685615099955UL;
                            }
                        }
                        raidData.Members.Add(new RaidDataMember(component.User.Id, job, component.Data.CustomId == "helpout", isSprout, isMentor));
                        CleanSaveRaids();
                        await component.UpdateAsync(m => m.Embed = BuildEmbed(raidData));
                    }
                    else
                    {
                        await SelectClassFollowup(component);
                    }
                }
                else
                {
                    await component.RespondAsync("Error: couldn't find raid data", ephemeral: true);
                }
                break;
            case "withdraw":
                if (Raids.Data.TryGetValue(component.Message.Id, out var raidData2))
                {
                    raidData2.Members.RemoveAll(m => m.UserId == component.User.Id);
                    CleanSaveRaids();
                    await component.UpdateAsync(m => m.Embed = BuildEmbed(raidData2));
                }
                else
                {
                    await component.RespondAsync("Error: couldn't find raid data", ephemeral: true);
                }
                break;
            case "resetclass":
                var ok = UserJobs.Data.Remove(component.User.Id);
                UserJobs.Save();
                await component.RespondAsync(ok ? "Class reset!" : "You already don't have a class selected", ephemeral: true);
                break;
            case "ping":
                if (component.User is SocketGuildUser user)
                {
                    // if (user.Roles.All(r => r.Id != 1208606814770565131UL && r.Id != 1208599020134600734))
                    // {
                    //     await component.RespondAsync("Only mentors can ping events!", ephemeral: true);
                    // }
                }
                if (Raids.Data.TryGetValue(component.Message.Id, out var raidData3))
                {
                    var pings = string.Join(", ", raidData3.Members.Select(m => MentionUtils.MentionUser(m.UserId)));
                    await component.RespondAsync($"Ping for the raiders for {raidData3.Title}: {pings}", ephemeral: false);
                }
                else
                {
                    await component.RespondAsync("Error: couldn't find raid data", ephemeral: true);
                }
                break;
        }
        foreach (var job in Jobs)
        {
            if (component.Data.CustomId == job.Id)
            {
                UserJobs.Data[component.User.Id] = job.Id;
                UserJobs.Save();
                await component.RespondAsync("Job selected! Sign up for raids now", ephemeral: true);
            }
        }
    }

    private void CleanSaveRaids()
    {
        List<ulong>? outdated = null;
        var deleteBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400;
        foreach (var (messageId, raid) in Raids.Data)
        {
            if (raid.Time < deleteBefore)
            {
                outdated ??= [];
                outdated.Add(messageId);
            }
            else
            {
                static int IndexOfJob(string job)
                {
                    for (int i = 0; i < Jobs.Length; i++)
                        if (Jobs[i].Id == job)
                            return i;
                    return -1;
                }
                // whoops this is horrible runtime
                raid.Members.Sort((a, b) => IndexOfJob(a.Job).CompareTo(IndexOfJob(b.Job)));
            }
        }
        Raids.Save();
    }

    private static async Task SelectClassFollowup(SocketMessageComponent component)
    {
        var builder = new ComponentBuilder();

        for (int i = 0; i < Jobs.Length; i++)
        {
            var job = Jobs[i];
            builder.WithButton(job.Name, job.Id, ButtonStyle.Secondary);
        }
        await component.RespondAsync("You don't have a job selected - first, select your job, then try again", ephemeral: true, components: builder.Build());
    }

    private Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        Raids.Data.Remove(message.Id);
        CleanSaveRaids();
        return Task.CompletedTask;
    }

}
