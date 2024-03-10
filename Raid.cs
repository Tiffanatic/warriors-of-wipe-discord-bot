using Discord;
using Discord.WebSocket;
using System.Globalization;
using System.Text.RegularExpressions;

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
internal class RaidData(string title, bool hasPinged, long time, ulong channel, ulong voiceChannel, ContentComp comp, List<RaidDataMember> members)
{
    public string Title = title;
    public bool hasPinged = hasPinged;
    public long Time = time;
    public ulong Channel = channel;
    public ulong VoiceChannel = voiceChannel;
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

internal partial class Raid
{
    public static readonly SlashCommandProperties[] Commands =
    [
        new SlashCommandBuilder()
            .WithName("raidcreate")
            .WithDescription("Create a 8-person raid")
            .AddOption("raid", ApplicationCommandOptionType.String, "The name of the raid to create", isRequired: true)
            .AddOption("time", ApplicationCommandOptionType.String, "Time (in server time): yyyy-MM-dd hh:mm", isRequired: true)
            .AddOption("voicechannel", ApplicationCommandOptionType.Channel, "Voice channel the raid will be in (start typing to filter channels)", isRequired: false)
            .Build(),

        new SlashCommandBuilder()
            .WithName("raidcreatelightparty")
            .WithDescription("Create a raid for light party (4 person) content")
            .AddOption("raid", ApplicationCommandOptionType.String, "The name of the raid to create", isRequired: true)
            .AddOption("time", ApplicationCommandOptionType.String, "Time (in server time): yyyy-MM-dd hh:mm", isRequired: true)
            .AddOption("voicechannel", ApplicationCommandOptionType.Channel, "Voice channel the raid will be in (start typing to filter channels)", isRequired: false)
            .Build(),

        new SlashCommandBuilder()
            .WithName("wipebotadmin")
            .WithDescription("Administrate wipebot stuff")
            // TODO: Permissions
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("signup")
                .WithDescription("Signs up a user to a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String, "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("user", ApplicationCommandOptionType.User, "The user to sign up", isRequired: true)
                .AddOption("job", ApplicationCommandOptionType.String, "The job the user should use", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("helper")
                .WithDescription("Signs up a user to a raid as a helper")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String, "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("user", ApplicationCommandOptionType.User, "The user to sign up", isRequired: true)
                .AddOption("job", ApplicationCommandOptionType.String, "The job the user should use", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("withdraw")
                .WithDescription("Withdraws a user from a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String, "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("user", ApplicationCommandOptionType.User, "The user to withdraw", isRequired: true)
            )
            .Build(),
    ];

    public const string PlaceholderDash = "⸺";
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
    private const int PingTimeFuture = 1800;
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
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("resetclass").WithStyle(ButtonStyle.Secondary).WithLabel("Choose class").Build());
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
        if (raidData.VoiceChannel != 0)
        {
            embed.Description += $"\nWill be in {MentionUtils.MentionChannel(raidData.VoiceChannel)} <t:{raidData.Time}:R>";
        }
        else
        {
            embed.Description += $"\n<t:{raidData.Time}:R>";
        }

        var playerList = raidData.Members.Where(m => !m.Helper).ToList();
        var players = string.Join("\n", RaidComp.FormatPlayerList(playerList, raidData.Comp));
        var helpers = string.Join("\n", raidData.Members.Where(m => m.Helper).Select(FormatMember));
        embed.AddField($"Confirmed raiders ({playerList.Count}/{raidData.Comp.Count})", string.IsNullOrWhiteSpace(players) ? PlaceholderDash : players, true);
        embed.AddField("Helpers available", string.IsNullOrWhiteSpace(helpers) ? PlaceholderDash : helpers, true);
        return embed.Build();
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName is "wipebotadmin")
        {
            await WipeBotAdmin(command);
            return;
        }
        if (command.CommandName is not ("raidcreate" or "raidcreatelightparty"))
            return;
        var comp = command.CommandName == "raidcreatelightparty" ? new ContentComp(1, 1, 2) : new ContentComp(2, 2, 4);
        var options = command.Data.Options.ToList();
        if (options.Count is not (2 or 3))
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
        var voiceChannel = options.Count > 2 ? (IChannel)options[2].Value : null;
        if (voiceChannel is not null and not IVoiceChannel)
        {
            await command.RespondAsync("Raid channel location must be a voice channel", ephemeral: true);
            return;
        }
        var hasPinged = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + PingTimeFuture > timestamp;
        var raidData = new RaidData(title, hasPinged, timestamp, command.ChannelId ?? 0, voiceChannel?.Id ?? 0, comp, []);
        var channel = await command.GetChannelAsync();
        var message = await channel.SendMessageAsync(allowedMentions: new AllowedMentions(AllowedMentionTypes.None), components: BuildMessageComponents(), embed: BuildEmbed(raidData));
        Raids.Data.Add(message.Id, raidData);
        CleanSaveRaids();
        await command.RespondAsync("Created", ephemeral: true);
    }

    private (bool isSprout, bool isMentor) IsMentorSprout(IUser user)
    {
        bool isMentor = false, isSprout = false;
        if (user is IGuildUser guildUser)
        {
            foreach (var role in guildUser.RoleIds)
            {
                isMentor |= role == MentorRoleId;
                isSprout |= role == SproutRoleId;
            }
        }
        return (isSprout, isMentor);
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
                        (bool isMentor, bool isSprout) = IsMentorSprout(component.User);
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
                        await SelectClassFollowup(component, "You don't have a job selected - first, select your job, then try again");
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
                await SelectClassFollowup(component, "Choose your class");
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

    private static async Task SelectClassFollowup(SocketMessageComponent component, string message)
    {
        var builder = new ComponentBuilder();

        for (int i = 0; i < Jobs.Length; i++)
        {
            var job = Jobs[i];
            builder.WithButton(job.Name, job.Id, ButtonStyle.Secondary, Emote.Parse(job.Emote), row: job.Row);
        }
        await component.RespondAsync(message, ephemeral: true, components: builder.Build());
    }

    private Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        // TODO: This event doesn't seem to be called
        Raids.Data.Remove(message.Id);
        CleanSaveRaids();
        return Task.CompletedTask;
    }

    private async Task TickUpdate(int o, int n)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changed = false;
        foreach (var (message, raid) in Raids.Data)
        {
            if (!raid.hasPinged && now < raid.Time && raid.Time <= now + PingTimeFuture)
            {
                raid.hasPinged = true;
                changed = true;
                if (raid.Members.Count > 0 && await client.GetChannelAsync(raid.Channel) is IMessageChannel ch)
                {
                    // Don't send ping if the raid signup form has been deleted
                    var msg = ch.GetMessageAsync(message);
                    // TODO: Some debugging to make sure this works
                    if (msg == null)
                    {
                        Console.WriteLine("Raid ping: message is null");
                    }
                    else
                    {
                        Console.WriteLine("Raid ping: Was able to retrieve message");
                    }
                    if (msg is not null)
                    {
                        await ch.SendMessageAsync(PingText(raid));
                    }
                }
            }
        }
        if (changed)
            CleanSaveRaids();
    }

    [GeneratedRegex(@"^(?:https:\/\/discord\.com\/channels\/\d+\/\d+\/)?(?<message>\d+)$")]
    private static partial Regex ChannelRegex();

    private async Task WipeBotAdmin(SocketSlashCommand command)
    {
        var option = command.Data.Options.Single();
        var options = option.Options.ToArray();
        var raidId = (string)options[0];
        var user = (IUser)options[1].Value;
        var match = ChannelRegex().Match(raidId);
        if (!match.Success)
        {
            await command.RespondAsync("Invalid raid, use \"Copy Message Link\" or \"Copy Message ID\" and paste it in the raid field", ephemeral: true);
            return;
        }
        if (!ulong.TryParse(match.Groups["message"].Value, out var messageId) || !Raids.Data.TryGetValue(messageId, out var raid))
        {
            await command.RespondAsync("Couldn't find raid with id " + messageId, ephemeral: true);
            return;
        }
        if (await client.GetChannelAsync(raid.Channel) is not IMessageChannel ch)
        {
            await command.RespondAsync("Unable to fetch channel", ephemeral: true);
            return;
        }
        var messageDataRaw = await ch.GetMessageAsync(messageId);
        if (messageDataRaw == null || messageDataRaw is not IUserMessage messageData)
        {
            await command.RespondAsync("Unable to fetch message", ephemeral: true);
            return;
        }
        switch (option.Name)
        {
            case "signup":
            case "helper":
                Job? JobFromIdOrName(string jobId)
                {
                    foreach (var j in Jobs)
                        if (j.Id.Equals(jobId, StringComparison.InvariantCultureIgnoreCase) || j.Name.Equals(jobId, StringComparison.InvariantCultureIgnoreCase))
                            return j;
                    return null;
                }
                var jobText = (string)options[2].Value;
                var job = JobFromIdOrName(jobText);
                if (job == null)
                {
                    await command.RespondAsync("Invalid job " + jobText, ephemeral: true);
                    return;
                }
                (bool isMentor, bool isSprout) = IsMentorSprout(user);
                var raidDataMember = new RaidDataMember(user.Id, job.Value.Id, option.Name == "helper", isSprout, isMentor);

                if (!RaidComp.CanAddPlayer(raid.Members, raidDataMember, raid.Comp, user.Id))
                {
                    await command.RespondAsync($"There is no room in the party for a {raidDataMember.JobData?.Name}", ephemeral: true);
                    return;
                }
                raid.Members.RemoveAll(m => m.UserId == user.Id);
                raid.Members.Add(raidDataMember);
                CleanSaveRaids();
                await messageData.ModifyAsync(m => m.Embed = BuildEmbed(raid));
                await command.RespondAsync($"{user.Mention} as {job.Value.Name} added to {raid.Title}", ephemeral: true);
                break;
            case "withdraw":
                raid.Members.RemoveAll(m => m.UserId == user.Id);
                CleanSaveRaids();
                await messageData.ModifyAsync(m => m.Embed = BuildEmbed(raid));
                await command.RespondAsync($"{user.Mention} removed from {raid.Title}", ephemeral: true);
                break;
        }
    }
}
