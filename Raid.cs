using Discord;
using Discord.WebSocket;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace WarriorsOfWipeBot;

internal enum RoleType
{
    Tank,
    Healer,
    Dps,
    AllRounder,
}

internal readonly record struct Job(string Id, string Emote, string Name, RoleType RoleType, int Row)
{
    public readonly bool DuplicatesAllowed => Id is "TNK" or "HLR" or "DPS" or "ALR";
}

[Serializable]
internal class RaidData(
    string title,
    bool hasPinged,
    bool hasStarted,
    bool requiresMentor,
    long time,
    ulong creator,
    ulong channel,
    ulong voiceChannel,
    ContentComp comp,
    List<RaidDataMember> members)
{
    public string Title = title;
    public bool hasPinged = hasPinged;
    [OptionalField] public bool hasStarted = hasStarted;
    [OptionalField] public bool requiresMentor = requiresMentor;
    public long Time = time;
    [OptionalField] public ulong Creator = creator;
    public ulong Channel = channel;
    public ulong VoiceChannel = voiceChannel;
    public ContentComp Comp = comp;
    public List<RaidDataMember> Members = members;
}

[Serializable]
internal class RaidDataMember(ulong userId, string nick, string job, bool helper, bool sprout, bool mentor)
{
    public ulong UserId = userId;
    [OptionalField] public string Nick = nick;
    public string Job = job;
    public bool Helper = helper;
    public bool Sprout = sprout;
    public bool Mentor = mentor;

    [NonSerialized] private Job? _jobData;
    public Job? JobData => _jobData == null ? _jobData = Raid.JobFromId(Job) : _jobData;
}

internal partial class Raid
{
    public static readonly ApplicationCommandProperties[] Commands =
    [
        new SlashCommandBuilder()
            .WithName("raid")
            .WithDescription("Create a 8-person raid")
            .AddOption("raid", ApplicationCommandOptionType.String,
                "The name of the raid to create (which fight, prog point, etc.)", isRequired: true)
            .AddOption("time", ApplicationCommandOptionType.String, "Time (in server time): yyyy-MM-dd hh:mm",
                isRequired: true)
            .AddOption("voicechannel", ApplicationCommandOptionType.Channel,
                "Voice channel the raid will be in (start typing to filter channels)", isRequired: false)
            .Build(),

        new SlashCommandBuilder()
            .WithName("raidlightparty")
            .WithDescription("Create a raid for light party (4 person) content")
            .AddOption("raid", ApplicationCommandOptionType.String,
                "The name of the raid to create (which fight, prog point, etc.)", isRequired: true)
            .AddOption("time", ApplicationCommandOptionType.String, "Time (in server time): yyyy-MM-dd hh:mm",
                isRequired: true)
            .AddOption("voicechannel", ApplicationCommandOptionType.Channel,
                "Voice channel the raid will be in (start typing to filter channels)", isRequired: false)
            .Build(),

        new SlashCommandBuilder()
            .WithName("wipebotadmin")
            .WithDescription("Administrate wipebot stuff")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("signup")
                .WithDescription("Signs up a user to a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("user", ApplicationCommandOptionType.User, "The user to sign up", isRequired: true)
                .AddOption("job", ApplicationCommandOptionType.String, "The job the user should use", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("helper")
                .WithDescription("Signs up a user to a raid as a helper")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("user", ApplicationCommandOptionType.User, "The user to sign up", isRequired: true)
                .AddOption("job", ApplicationCommandOptionType.String, "The job the user should use", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("withdraw")
                .WithDescription("Withdraws a user from a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("user", ApplicationCommandOptionType.User, "The user to withdraw", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("whomadethis")
                .WithDescription("Shows who made a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("changetitle")
                .WithDescription("Changes the title of a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("title", ApplicationCommandOptionType.String, "The new title", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("changetime")
                .WithDescription("Changes the time of a raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
                .AddOption("time", ApplicationCommandOptionType.String, "The new time in server time", isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("clearsignups")
                .WithDescription("Removes everyone signed up from the raid")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("raid", ApplicationCommandOptionType.String,
                    "The raid - use \"Copy Message Link\" or \"Copy Message ID\" and paste it here", isRequired: true)
            )
            .Build(),

        new MessageCommandBuilder()
            .WithName(PingRaidMessageCommandName)
            .Build(),

        new MessageCommandBuilder()
            .WithName(DeleteRaidMessageCommandName)
            .Build(),

        new MessageCommandBuilder()
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .WithName(ToggleRequiresMentorCommandName)
            .Build(),
    ];

    private const string PingRaidMessageCommandName = "Ping /raid";
    private const string DeleteRaidMessageCommandName = "Delete /raid created by you";
    private const string ToggleRequiresMentorCommandName = "Toggle requires mentor";
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

        new("WHM", "<:WhiteMage:1215315454403289118>", "White Mage", RoleType.Healer, 1),
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
    public const string crown = "👑";
    private const int PingTimeFuture = 1800;
    private const ulong WarriorsOfWipeGuildId = 1208569487964643418UL;
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
        client.ModalSubmitted += ModalSubmitted;
        client.MessageCommandExecuted += MessageCommandExecuted;
        client.MessageDeleted += MessageDeleted;
        client.LatencyUpdated += TickUpdate;
    }

    private static MessageComponent BuildMessageComponents()
    {
        var components = new ComponentBuilder();
        ActionRowBuilder rowBuilder = new();
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("signup").WithStyle(ButtonStyle.Secondary)
            .WithLabel("Sign up").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("helpout").WithStyle(ButtonStyle.Secondary)
            .WithLabel("Available to help").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("withdraw").WithStyle(ButtonStyle.Secondary)
            .WithLabel("Withdraw").Build());
        rowBuilder.AddComponent(new ButtonBuilder().WithCustomId("resetclass").WithStyle(ButtonStyle.Secondary)
            .WithLabel("Choose class").Build());
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
        var name = string.IsNullOrEmpty(raidDataMember.Nick)
            ? MentionUtils.MentionUser(raidDataMember.UserId)
            : raidDataMember.Nick;
        return $"{jobEmote} {(raidDataMember.Mentor ? crown : "")}{(raidDataMember.Sprout ? sprout : "")}{name}";
    }

    private static string GetNick(IUser user) =>
        user is IGuildUser g ? g.DisplayName : user.GlobalName ?? user.Username;

    private static Embed BuildEmbed(RaidData raidData)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        EmbedBuilder embed = new()
        {
            Title = raidData.Title,
            Description = $"<t:{raidData.Time}:F>",
            Color = now < raidData.Time ? new(0x11ffaa) : new Color(0xff1155)
        };
        if (raidData.VoiceChannel != 0)
        {
            embed.Description +=
                $"\nWill be in {MentionUtils.MentionChannel(raidData.VoiceChannel)} <t:{raidData.Time}:R>";
        }
        else
        {
            embed.Description += $"\n<t:{raidData.Time}:R>";
        }

        var playerList = raidData.Members.Where(m => !m.Helper).ToList();
        var players = string.Join("\n", RaidComp.FormatPlayerList(playerList, raidData.Comp, raidData.requiresMentor));
        var helpers = string.Join("\n", raidData.Members.Where(m => m.Helper).Select(FormatMember));
        embed.AddField($"Confirmed raiders ({playerList.Count}/{raidData.Comp.Count})",
            string.IsNullOrWhiteSpace(players) ? PlaceholderDash : players, true);
        embed.AddField("Available if needed", string.IsNullOrWhiteSpace(helpers) ? PlaceholderDash : helpers, true);
        return embed.Build();
    }

    private static bool TryParseTime(string input, out DateTimeOffset time, out string message)
    {
        if (!DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out time))
        {
            message = "Invalid time format";
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (time < now)
        {
            message = "Cannot make a raid in the past";
            return false;
        }

        if (time > now.AddDays(14))
        {
            message = "Cannot make a raid more than two weeks from now";
            return false;
        }

        message = "";
        return true;
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName is "wipebotadmin")
        {
            await WipeBotAdmin(command);
            return;
        }

        if (command.CommandName is not ("raid" or "raidlightparty"))
            return;
        var comp = command.CommandName == "raidlightparty" ? new(1, 1, 2) : new ContentComp(2, 2, 4);
        var options = command.Data.Options.ToList();
        if (options.Count is not (2 or 3))
            return;
        var title = (string)options[0].Value;
        if (!TryParseTime((string)options[1].Value, out var time, out var timeErrorMessage))
        {
            await command.RespondAsync(timeErrorMessage, ephemeral: true);
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
        var raidData = new RaidData(title, hasPinged, false, false, timestamp, command.User.Id, command.ChannelId ?? 0,
            voiceChannel?.Id ?? 0, comp, []);
        var channel = await command.GetChannelAsync();
        var message = await channel.SendMessageAsync(allowedMentions: new(AllowedMentionTypes.None),
            components: BuildMessageComponents(), embed: BuildEmbed(raidData));
        Raids.Data.Add(message.Id, raidData);
        CleanSaveRaids();
        await command.RespondAsync("Created", ephemeral: true);
    }

    private static (bool isSprout, bool isMentor) IsMentorSprout(IUser user)
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
                        var (isSprout, isMentor) = IsMentorSprout(component.User);
                        var raidDataMember = new RaidDataMember(component.User.Id, GetNick(component.User), job,
                            component.Data.CustomId == "helpout", isSprout, isMentor);

                        if (RaidComp.CanAddPlayer(raidData.Members, raidDataMember, raidData.Comp, component.User.Id,
                                raidData.requiresMentor))
                        {
                            raidData.Members.RemoveAll(m => m.UserId == component.User.Id);
                            raidData.Members.Add(raidDataMember);
                            CleanSaveRaids();
                            await component.UpdateAsync(m =>
                            {
                                m.Embed = BuildEmbed(raidData);
                                m.Components = BuildMessageComponents();
                            });
                        }
                        else
                        {
                            await component.RespondAsync(
                                $"There is no room in the party for a {raidDataMember.JobData?.Name}", ephemeral: true);
                        }
                    }
                    else
                    {
                        await SelectClassFollowup(component,
                            "You don't have a job selected - first, select your job, then try again");
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
                    await component.UpdateAsync(m =>
                    {
                        m.Embed = BuildEmbed(raidData2);
                        m.Components = BuildMessageComponents();
                    });
                }
                else
                {
                    await component.RespondAsync("Error: couldn't find raid data", ephemeral: true);
                }

                break;
            case "resetclass":
                UserJobs.Data.Remove(component.User.Id);
                UserJobs.Save();
                await SelectClassFollowup(component, "Choose your class");
                break;
        }

        foreach (var job in Jobs)
        {
            if (component.Data.CustomId == job.Id)
            {
                UserJobs.Data[component.User.Id] = job.Id;
                UserJobs.Save();
                await component.UpdateAsync(m => m.Content = $"Selected {job.Emote}{job.Name}! Sign up for raids now");
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
                    for (var i = 0; i < Jobs.Length; i++)
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

        foreach (var job in Jobs)
            builder.WithButton(job.Name, job.Id, ButtonStyle.Secondary, Emote.Parse(job.Emote), row: job.Row);
        await component.RespondAsync(message, ephemeral: true, components: builder.Build());
    }

    private async Task MessageCommandExecuted(SocketMessageCommand command)
    {
        switch (command.CommandName)
        {
            case PingRaidMessageCommandName:
                await PingRaidMessageCommand(command);
                break;
            case DeleteRaidMessageCommandName:
                await DeleteRaidMessageCommand(command);
                break;
            case ToggleRequiresMentorCommandName:
                await ToggleRequiresMentorCommand(command);
                break;
        }
    }

    private async Task ModalSubmitted(SocketModal modal)
    {
        switch (modal.Data.CustomId)
        {
            case "ping":
                await PingModalSubmitted(modal);
                break;
        }
    }

    private async Task PingRaidMessageCommand(SocketMessageCommand command)
    {
        if (command is { User: IGuildUser user, GuildId: WarriorsOfWipeGuildId } &&
            user.RoleIds.All(r => r != MentorRoleId && r != ModRoleId))
        {
            await command.RespondAsync("Only mentors can ping events!", ephemeral: true);
        }
        else if (Raids.Data.TryGetValue(command.Data.Message.Id, out var raidData3))
        {
            if (raidData3.Members.Count == 0)
            {
                await command.RespondAsync("There's no one signed up to ping", ephemeral: true);
                return;
            }

            var modalBuilder = new ModalBuilder("Enter ping text", "ping");
            modalBuilder.AddTextInput("Ping text", command.Data.Message.Id.ToString(), required: false);
            await command.RespondWithModalAsync(modalBuilder.Build());
        }
        else
        {
            await command.RespondAsync("Error: couldn't find raid data", ephemeral: true);
        }
    }

    private async Task PingModalSubmitted(SocketModal modal)
    {
        var textInput = modal.Data.Components.Single();
        var id = ulong.Parse(textInput.CustomId);
        if (Raids.Data.TryGetValue(id, out var raidData))
        {
            await modal.RespondAsync($"Ping from {modal.User.Mention}: {textInput.Value}\n{PingText(raidData, false)}",
                ephemeral: false);
        }
        else
        {
            await modal.RespondAsync("Raid not found", ephemeral: true);
        }
    }

    private static string PingText(RaidData raidData, bool is30MinPing)
    {
        var players = string.Join(", ",
            raidData.Members.Where(m => !m.Helper).Select(m => MentionUtils.MentionUser(m.UserId)));
        var helpers = string.Join(", ",
            raidData.Members.Where(m => m.Helper).Select(m => MentionUtils.MentionUser(m.UserId)));
        var msg = $"{raidData.Title} starts <t:{raidData.Time}:R>: {players}";
        if (!string.IsNullOrEmpty(helpers))
        {
            if (is30MinPing)
            {
                msg += $"\nHelpers, please `Sign up` now to confirm your slot: {helpers}";
            }
            else
            {
                msg += $" (and helpers {helpers})";
            }
        }

        return msg;
    }

    private async Task DeleteRaidMessageCommand(SocketMessageCommand command)
    {
        if (Raids.Data.TryGetValue(command.Data.Message.Id, out var raidData))
        {
            if (command.User.Id == raidData.Creator)
            {
                await (await command.GetChannelAsync()).DeleteMessageAsync(command.Data.Message.Id);
                await command.RespondAsync("Deleted", ephemeral: true);
            }
            else
            {
                await command.RespondAsync("You are not the creator of this raid", ephemeral: true);
            }
        }
        else
        {
            await command.RespondAsync("This is not a raid signup form created with /raid", ephemeral: true);
        }
    }

    private async Task ToggleRequiresMentorCommand(SocketMessageCommand command)
    {
        if (Raids.Data.TryGetValue(command.Data.Message.Id, out var raidData))
        {
            raidData.requiresMentor = !raidData.requiresMentor;
            CleanSaveRaids();
            if (command.Data.Message is IUserMessage message)
            {
                // discord.net is so bad, omfg
                if (message.Channel == null)
                    message = await (await command.GetChannelAsync()).GetMessageAsync(message.Id) as IUserMessage ??
                              message;
                await message.ModifyAsync(m =>
                {
                    m.Embed = BuildEmbed(raidData);
                    m.Components = BuildMessageComponents();
                });
            }

            await command.RespondAsync(
                raidData.requiresMentor ? "Raid set to now require a mentor" : "Raid no longer requires a mentor",
                ephemeral: true);
        }
        else
        {
            await command.RespondAsync("This is not a raid signup form created with /raid", ephemeral: true);
        }
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
                    var msg = await ch.GetMessageAsync(message);
                    if (msg is not null)
                    {
                        Console.WriteLine("Raid ping: " + raid.Title);
                        await ch.SendMessageAsync(PingText(raid, true));
                    }
                }
            }

            if (!raid.hasStarted && now > raid.Time)
            {
                Console.WriteLine("Starting raid (for color) " + raid.Title);
                raid.hasStarted = true;
                changed = true;
                if (await client.GetChannelAsync(raid.Channel) is IMessageChannel ch &&
                    await ch.GetMessageAsync(message) is IUserMessage msg)
                {
                    await msg.ModifyAsync(m =>
                    {
                        m.Embed = BuildEmbed(raid);
                        m.Components = BuildMessageComponents();
                    });
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

        async Task<RaidData?> GetRaidData(int argument)
        {
            var raidId = (string)options[argument];
            var match = ChannelRegex().Match(raidId);
            if (!match.Success)
            {
                await command.RespondAsync(
                    "Invalid raid, use \"Copy Message Link\" or \"Copy Message ID\" and paste it in the raid field",
                    ephemeral: true);
                return null;
            }

            if (!ulong.TryParse(match.Groups["message"].Value, out var messageId) ||
                !Raids.Data.TryGetValue(messageId, out var raid))
            {
                await command.RespondAsync("Couldn't find raid with id " + messageId, ephemeral: true);
                return null;
            }

            return raid;
        }

        async Task<(IUserMessage messageData, RaidData raid)?> GetRaid(int argument)
        {
            var raidId = (string)options[argument];
            var match = ChannelRegex().Match(raidId);
            if (!match.Success)
            {
                await command.RespondAsync(
                    "Invalid raid, use \"Copy Message Link\" or \"Copy Message ID\" and paste it in the raid field",
                    ephemeral: true);
                return null;
            }

            if (!ulong.TryParse(match.Groups["message"].Value, out var messageId) ||
                !Raids.Data.TryGetValue(messageId, out var raid))
            {
                await command.RespondAsync("Couldn't find raid with id " + messageId, ephemeral: true);
                return null;
            }

            if (await client.GetChannelAsync(raid.Channel) is not IMessageChannel ch)
            {
                await command.RespondAsync("Unable to fetch channel", ephemeral: true);
                return null;
            }

            var messageDataRaw = await ch.GetMessageAsync(messageId);
            if (messageDataRaw == null || messageDataRaw is not IUserMessage messageData)
            {
                await command.RespondAsync("Unable to fetch message", ephemeral: true);
                return null;
            }

            return (messageData, raid);
        }

        switch (option.Name)
        {
            case "signup":
            case "helper":
                {
                    static Job? JobFromIdOrName(string jobId)
                    {
                        foreach (var j in Jobs)
                            if (j.Id.Equals(jobId, StringComparison.InvariantCultureIgnoreCase) ||
                                j.Name.Equals(jobId, StringComparison.InvariantCultureIgnoreCase))
                                return j;
                        return null;
                    }

                    var raidResult = await GetRaid(0);
                    if (!raidResult.HasValue)
                        return;
                    var (messageData, raid) = raidResult.Value;
                    var user = (IUser)options[1].Value;
                    var jobText = (string)options[2].Value;
                    var job = JobFromIdOrName(jobText);
                    if (job == null)
                    {
                        await command.RespondAsync("Invalid job " + jobText, ephemeral: true);
                        return;
                    }

                    var (isSprout, isMentor) = IsMentorSprout(user);
                    var raidDataMember = new RaidDataMember(user.Id, GetNick(user), job.Value.Id,
                        option.Name == "helper", isSprout, isMentor);

                    if (!RaidComp.CanAddPlayer(raid.Members, raidDataMember, raid.Comp, user.Id, raid.requiresMentor))
                    {
                        await command.RespondAsync(
                            $"There is no room in the party for a {raidDataMember.JobData?.Name}", ephemeral: true);
                        return;
                    }

                    raid.Members.RemoveAll(m => m.UserId == user.Id);
                    raid.Members.Add(raidDataMember);
                    CleanSaveRaids();
                    await messageData.ModifyAsync(m =>
                    {
                        m.Embed = BuildEmbed(raid);
                        m.Components = BuildMessageComponents();
                    });
                    await command.RespondAsync($"{user.Mention} as {job.Value.Name} added to {raid.Title}",
                        ephemeral: true);
                    break;
                }
            case "withdraw":
                {
                    var raidResult = await GetRaid(0);
                    if (!raidResult.HasValue)
                        return;
                    var (messageData, raid) = raidResult.Value;
                    var user = (IUser)options[1].Value;
                    raid.Members.RemoveAll(m => m.UserId == user.Id);
                    CleanSaveRaids();
                    await messageData.ModifyAsync(m =>
                    {
                        m.Embed = BuildEmbed(raid);
                        m.Components = BuildMessageComponents();
                    });
                    await command.RespondAsync($"{user.Mention} removed from {raid.Title}", ephemeral: true);
                    break;
                }
            case "whomadethis":
                {
                    var raid = await GetRaidData(0);
                    if (raid == null)
                        return;
                    await command.RespondAsync($"{MentionUtils.MentionUser(raid.Creator)}", ephemeral: true);
                }
                break;
            case "changetitle":
                {
                    var raidResult = await GetRaid(0);
                    if (!raidResult.HasValue)
                        return;
                    var (messageData, raid) = raidResult.Value;
                    var title = (string)options[1].Value;
                    raid.Title = title;
                    CleanSaveRaids();
                    await messageData.ModifyAsync(m =>
                    {
                        m.Embed = BuildEmbed(raid);
                        m.Components = BuildMessageComponents();
                    });
                    await command.RespondAsync($"Title changed to " + title, ephemeral: true);
                }
                break;
            case "changetime":
                {
                    var raidResult = await GetRaid(0);
                    if (!raidResult.HasValue)
                        return;
                    var (messageData, raid) = raidResult.Value;
                    if (!TryParseTime((string)options[1].Value, out var time, out var timeErrorMessage))
                    {
                        await command.RespondAsync(timeErrorMessage, ephemeral: true);
                        return;
                    }

                    raid.Time = time.ToUnixTimeSeconds();
                    CleanSaveRaids();
                    await messageData.ModifyAsync(m =>
                    {
                        m.Embed = BuildEmbed(raid);
                        m.Components = BuildMessageComponents();
                    });
                    await command.RespondAsync($"Time changed to <t:{raid.Time}:F>", ephemeral: true);
                }
                break;
            case "clearsignups":
                {
                    var raidResult = await GetRaid(0);
                    if (!raidResult.HasValue)
                        return;
                    var (messageData, raid) = raidResult.Value;
                    raid.Members.Clear();
                    CleanSaveRaids();
                    await messageData.ModifyAsync(m =>
                    {
                        m.Embed = BuildEmbed(raid);
                        m.Components = BuildMessageComponents();
                    });
                    await command.RespondAsync($"Players cleared", ephemeral: true);
                }
                break;
        }
    }
}
