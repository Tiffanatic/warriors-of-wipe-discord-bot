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

    private record struct Job(string Id, string Emote, string Name, int Row);

    // warriors of wipe original:
    // <:Tank:1211492267441922048>
    // <:Healer:1211492332193579019>
    // <:DPS:1211492203466068078>

    private static readonly Job[] Jobs =
    [
        new("PLD", "<:Paladin:1215315435382382663>", "Paladin", 0),
        new("WAR", "<:Warrior:1215315451408818216>", "Warrior", 0),
        new("DRK", "<:DarkKnight:1215315422744674437>", "Dark Knight", 0),
        new("GNB", "<:Gunbreaker:1215315427266265088>", "Gunbreaker", 0),
        new("TNK", "<:Tank:1211492267441922048>", "Omni-tank", 0),

        new("WHM", "<:WhiteMage:1215315454403289118>", "White Mage", 1),
        new("SCH", "<:Scholar:1215315447440998452>", "Scholar", 1),
        new("AST", "<:Astrologian:1215315415547510804>", "Astrologian", 1),
        new("SGE", "<:Sage:1215315441866776617>", "Sage", 1),
        new("HLR", "<:Healer:1211492332193579019>", "Omni-healer", 1),

        new("MNK", "<:Monk:1215315431435272222>", "Monk", 2),
        new("DRG", "<:Dragoon:1215315425286430730>", "Dragoon", 2),
        new("NIN", "<:Ninja:1215315433414987887>", "Ninja", 2),
        new("SAM", "<:Samurai:1215315444362125364>", "Samurai", 2),
        new("RPR", "<:Reaper:1215315437743505448>", "Reaper", 2),

        new("BRD", "<:Bard:1215315416805802035>", "Bard", 3),
        new("MCH", "<:Machinist:1215315429397102613>", "Machinist", 3),
        new("DNC", "<:Dancer:1215315420668764231>", "Dancer", 3),

        new("BLM", "<:BlackMage:1215315418563223592>", "Black Mage", 4),
        new("SMN", "<:Summoner:1215315493561307176>", "Summoner", 4),
        new("RDM", "<:RedMage:1215315492198293534>", "Red Mage", 4),
        new("DPS", "<:DPS:1211492203466068078>", "Omni-dps", 4),

        new("ALR", "<:Allrounder:1215319950747508736>", "All-rounder", 4),
    ];

    private const string sprout = "🌱";
    private const string crown = "👑";
    private const ulong MentorRoleId = 1208606814770565131UL;
    private const ulong ModRoleId = 1208599020134600734UL;
    private const ulong SproutRoleId = 1208606685615099955UL;
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
                        if (component.User is IGuildUser guildUser)
                        {
                            foreach (var role in guildUser.RoleIds)
                            {
                                isMentor |= role == MentorRoleId;
                                isSprout |= role == SproutRoleId;
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
                if (component.User is IGuildUser user && user.RoleIds.All(r => r != MentorRoleId && r != ModRoleId))
                {
                    await component.RespondAsync("Only mentors can ping events!", ephemeral: true);
                }
                else if (Raids.Data.TryGetValue(component.Message.Id, out var raidData3))
                {
                    if (raidData3.Members.Count == 0)
                    {
                        await component.RespondAsync("There's no one signed up to ping", ephemeral: true);
                        return;
                    }
                    var hasHelpers = raidData3.Members.Any(m => m.Helper);
                    var players = string.Join(", ", raidData3.Members.Where(m => !m.Helper).Select(m => MentionUtils.MentionUser(m.UserId)));
                    var helpers = string.Join(", ", raidData3.Members.Where(m => m.Helper).Select(m => MentionUtils.MentionUser(m.UserId)));
                    await component.RespondAsync($"Ping! {raidData3.Title}: {players}{(hasHelpers ? $" (and helpers {helpers})" : "")}", ephemeral: false);
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
            builder.WithButton(job.Name, job.Id, ButtonStyle.Secondary, Emote.Parse(job.Emote), row: job.Row);
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
