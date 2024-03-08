using Discord;
using Discord.WebSocket;
using System.Globalization;

namespace WarriorsOfWipeBot;

internal enum RoleType
{
    Tank,
    Healer,
    Dps,
    AllRounder,
}

internal record struct Job(string Id, string Emote, string Name, RoleType RoleType, int Row);

[Serializable]
internal class RaidData(string title, long time, ulong channel, ContentComp comp, List<RaidDataMember> members)
{
    public string Title = title;
    public long Time = time;
    public ulong Channel = channel;
    public ContentComp Comp = comp;
    public List<RaidDataMember> Members = members;
}

[Serializable]
internal class RaidDataMember(ulong userId, string job, bool helper, bool sprout, bool mentor)
{
    public ulong UserId = userId;
    public string Job = job;
    public bool Helper = helper;
    public bool Sprout = sprout;
    public bool Mentor = mentor;

    [NonSerialized]
    private Job? _jobData;
    public Job? JobData => _jobData == null ? _jobData = Raid.JobFromId(Job) : _jobData;
}

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

    public const string TankEmote = "<:Tank:1211492267441922048>";
    public const string HealerEmote = "<:Healer:1211492332193579019>";
    public const string DpsEmote = "<:DPS:1211492203466068078>";
    private static readonly Job[] Jobs =
    [
        new("PLD", "<:Paladin:1215315435382382663>", "Paladin", RoleType.Tank, 0),
        new("WAR", "<:Warrior:1215315451408818216>", "Warrior", RoleType.Tank, 0),
        new("DRK", "<:DarkKnight:1215315422744674437>", "Dark Knight", RoleType.Tank, 0),
        new("GNB", "<:Gunbreaker:1215315427266265088>", "Gunbreaker", RoleType.Tank, 0),
        new("TNK", TankEmote, "Omni-tank", RoleType.Tank, 0),

        new("WHM", "<:WhiteMage:1215315454403289118>", "White Mage", RoleType.Healer,1),
        new("SCH", "<:Scholar:1215315447440998452>", "Scholar", RoleType.Healer, 1),
        new("AST", "<:Astrologian:1215315415547510804>", "Astrologian", RoleType.Healer, 1),
        new("SGE", "<:Sage:1215315441866776617>", "Sage", RoleType.Healer, 1),
        new("HLR", HealerEmote, "Omni-healer", RoleType.Healer, 1),

        new("MNK", "<:Monk:1215315431435272222>", "Monk", RoleType.Dps, 2),
        new("DRG", "<:Dragoon:1215315425286430730>", "Dragoon", RoleType.Dps, 2),
        new("NIN", "<:Ninja:1215315433414987887>", "Ninja", RoleType.Dps, 2),
        new("SAM", "<:Samurai:1215315444362125364>", "Samurai", RoleType.Dps, 2),
        new("RPR", "<:Reaper:1215315437743505448>", "Reaper", RoleType.Dps, 2),

        new("BRD", "<:Bard:1215315416805802035>", "Bard", RoleType.Dps, 3),
        new("MCH", "<:Machinist:1215315429397102613>", "Machinist", RoleType.Dps, 3),
        new("DNC", "<:Dancer:1215315420668764231>", "Dancer", RoleType.Dps, 3),

        new("BLM", "<:BlackMage:1215315418563223592>", "Black Mage", RoleType.Dps, 4),
        new("SMN", "<:Summoner:1215315493561307176>", "Summoner", RoleType.Dps, 4),
        new("RDM", "<:RedMage:1215315492198293534>", "Red Mage", RoleType.Dps, 4),
        new("DPS", DpsEmote, "Omni-dps", RoleType.Dps, 4),

        new("ALR", "<:Allrounder:1215319950747508736>", "All-rounder", RoleType.AllRounder, 4),
    ];

    private const string sprout = "🌱";
    private const string crown = "👑";
    private const ulong MentorRoleId = 1208606814770565131UL;
    private const ulong ModRoleId = 1208599020134600734UL;
    private const ulong SproutRoleId = 1208606685615099955UL;
    private readonly DiscordSocketClient client;
    private readonly Json<Dictionary<ulong, RaidData>> Raids = new("raid.json");
    private readonly Json<Dictionary<ulong, string>> UserJobs = new("userjobs.json");

    public Raid(DiscordSocketClient client)
    {
        this.client = client;
        client.SlashCommandExecuted += SlashCommandExecuted;
        client.ButtonExecuted += ButtonExecuted;
        client.MessageDeleted += MessageDeleted;
        client.LatencyUpdated += TickUpdate;
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

    public static Job? JobFromId(string jobId)
    {
        foreach (var j in Jobs)
            if (j.Id == jobId)
                return j;
        return null;
    }

    public static string FormatMember(RaidDataMember raidDataMember)
    {
        var jobEmote = JobFromId(raidDataMember.Job)?.Emote ?? raidDataMember.Job;
        return $"{jobEmote} {(raidDataMember.Mentor ? crown : "")}{(raidDataMember.Sprout ? sprout : "")}{MentionUtils.MentionUser(raidDataMember.UserId)}";
    }

    private static Embed BuildEmbed(RaidData raidData)
    {
        EmbedBuilder embed = new()
        {
            Title = raidData.Title,
            Description = $"<t:{raidData.Time}:F>",
            Color = new Color(0xff1155)
        };

        var playerList = raidData.Members.Where(m => !m.Helper).ToList();
        var players = string.Join("\n", RaidComp.FormatPlayerList(playerList, raidData.Comp));
        var helpers = string.Join("\n", raidData.Members.Where(m => m.Helper).Select(FormatMember));
        embed.AddField($"Confirmed raiders ({playerList.Count}/{raidData.Comp.Count})", string.IsNullOrWhiteSpace(players) ? "---" : players, true);
        embed.AddField("Helpers available", string.IsNullOrWhiteSpace(helpers) ? "---" : helpers, true);
        return embed.Build();
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName != "raidcreate")
            return;
        // TODO: Comp selection
        var comp = new ContentComp(2, 2, 4);
        // TODO: Voice channel linking
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
        var raidData = new RaidData(title, timestamp, command.ChannelId ?? 0, comp, []);
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
                        bool isMentor = false, isSprout = false;
                        if (component.User is IGuildUser guildUser)
                        {
                            foreach (var role in guildUser.RoleIds)
                            {
                                isMentor |= role == MentorRoleId;
                                isSprout |= role == SproutRoleId;
                            }
                        }
                        var raidDataMember = new RaidDataMember(component.User.Id, job, component.Data.CustomId == "helpout", isSprout, isMentor);

                        if (RaidComp.CanAddPlayer(raidData.Members, raidDataMember, raidData.Comp, component.User.Id))
                        {
                            raidData.Members.RemoveAll(m => m.UserId == component.User.Id);
                            raidData.Members.Add(raidDataMember);
                            CleanSaveRaids();
                            await component.UpdateAsync(m => m.Embed = BuildEmbed(raidData));
                        }
                        else
                        {
                            await component.RespondAsync($"There is no room in the party for a {raidDataMember.JobData?.Name}", ephemeral: true);
                        }
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
                    await component.RespondAsync($"Ping! {PingText(raidData3)}", ephemeral: false);
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

    private string PingText(RaidData raidData)
    {
        var hasHelpers = raidData.Members.Any(m => m.Helper);
        var players = string.Join(", ", raidData.Members.Where(m => !m.Helper).Select(m => MentionUtils.MentionUser(m.UserId)));
        var helpers = string.Join(", ", raidData.Members.Where(m => m.Helper).Select(m => MentionUtils.MentionUser(m.UserId)));
        return $"{raidData.Title} starts <t:{raidData.Time}:R>: {players}{(hasHelpers ? $" (and helpers {helpers})" : "")}";
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
        if (outdated is not null)
        {
            foreach (var outd in outdated)
            {
                Raids.Data.Remove(outd);
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

    // TODO: Drops raid pings when bot is offline
    private long oldTickUpdate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private async Task TickUpdate(int o, int n)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var (_, raid) in Raids.Data)
        {
            const int future = 1800;
            if (raid.Members.Count > 0 && now < raid.Time && oldTickUpdate + future < raid.Time && raid.Time <= now + future)
            {
                if (await client.GetChannelAsync(raid.Channel) is IMessageChannel ch)
                {
                    await ch.SendMessageAsync(PingText(raid));
                }
            }
        }
        oldTickUpdate = now;
    }
}
