using Discord;
using Discord.WebSocket;

namespace WarriorsOfWipeBot;

public class Omegapoll
{
    private const ulong WarriorsOfWipeGuildId = 1208569487964643418UL;

    public static readonly ApplicationCommandProperties[] Commands =
    [
        new SlashCommandBuilder()
            .WithName("omegapoll")
            .WithDescription("Create batches of polls")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("create")
                .WithDescription("Creates a saved batch of polls")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to send",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("makebatch")
                .WithDescription("Creates a new poll batch configuration")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to create",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("deletebatch")
                .WithDescription("Deletes a poll batch configuration")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to delete",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("showbatch")
                .WithDescription("Prints info about a poll batch")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to show",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("listbatch")
                .WithDescription("Lists all batches")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("add")
                .WithDescription("Adds a new poll to a poll batch")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to add to",
                    isRequired: true)
                .AddOption("question", ApplicationCommandOptionType.String, "The title of the poll", isRequired: true)
                .AddOption("choices", ApplicationCommandOptionType.String, "Enter choices seperated by either | or ,",
                    isRequired: true)
                .AddOption("duration", ApplicationCommandOptionType.Integer, "Duration, in hours", isRequired: true)
                .AddOption("multipleanswers", ApplicationCommandOptionType.Boolean, "Allow multiple answers",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("insert")
                .WithDescription("Inserts a new poll into a poll batch at the index")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to add to",
                    isRequired: true)
                .AddOption("index", ApplicationCommandOptionType.Integer,
                    "The index of the poll within the poll batch you want to insert at", isRequired: true)
                .AddOption("question", ApplicationCommandOptionType.String, "The title of the poll", isRequired: true)
                .AddOption("choices", ApplicationCommandOptionType.String, "Enter choices seperated by either | or ,",
                    isRequired: true)
                .AddOption("duration", ApplicationCommandOptionType.Integer, "Duration, in hours", isRequired: true)
                .AddOption("multipleanswers", ApplicationCommandOptionType.Boolean, "Allow multiple answers",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("edit")
                .WithDescription("Edits a poll in a poll batch")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to add to",
                    isRequired: true)
                .AddOption("index", ApplicationCommandOptionType.Integer,
                    "The index of the poll within the poll batch you want to edit", isRequired: true)
                .AddOption("question", ApplicationCommandOptionType.String, "The title of the poll", isRequired: true)
                .AddOption("choices", ApplicationCommandOptionType.String, "Enter choices seperated by either | or ,",
                    isRequired: true)
                .AddOption("duration", ApplicationCommandOptionType.Integer, "Duration, in hours", isRequired: true)
                .AddOption("multipleanswers", ApplicationCommandOptionType.Boolean, "Allow multiple answers",
                    isRequired: true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("remove")
                .WithDescription("Removes a poll from a poll batch")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "The name of the poll batch to add to",
                    isRequired: true)
                .AddOption("index", ApplicationCommandOptionType.Integer,
                    "The index of the poll within the poll batch you want to remove", isRequired: true)
            )
            .Build()
    ];

    private readonly DiscordSocketClient client;
    private readonly Json<Dictionary<string, List<PollEntry>>> Polls = new("polls.json");

    [Serializable]
    internal class PollEntry(string question, string[] choices, uint duration, bool allowMultiselect)
    {
        public string Question = question;
        public string[] Choices = choices;
        public uint Duration = duration;
        public bool AllowMultiselect = allowMultiselect;

        public PollProperties ToProps() => new()
        {
            Question = new() { Text = Question },
            Answers = Choices.Select(c => new PollMediaProperties { Text = c }).ToList(),
            Duration = Duration,
            AllowMultiselect = AllowMultiselect,
            LayoutType = PollLayout.Default
        };

        public override string ToString()
        {
            return $"{Question} (duration={Duration}, multi={AllowMultiselect}): {string.Join("|", Choices)}";
        }

        public static string ToString(List<PollEntry> list)
        {
            return string.Join("\n", list.Select((p, i) => $"poll {i} - {p}"));
        }
    }

    public Omegapoll(DiscordSocketClient client)
    {
        this.client = client;
        client.SlashCommandExecuted += SlashCommandExecuted;
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName is not "omegapoll" || command.GuildId != WarriorsOfWipeGuildId)
            return;

        var option = command.Data.Options.Single();
        var options = option.Options.ToArray();

        switch (option.Name)
        {
            case "create":
                {
                    var name = (string)options[0].Value;
                    if (Polls.Data.TryGetValue(name, out var list))
                    {
                        var channel = await command.GetChannelAsync();
                        foreach (var poll in list)
                            await channel.SendMessageAsync(poll: poll.ToProps());
                    }
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                }
                break;
            case "makebatch":
                {
                    var name = (string)options[0].Value;
                    if (Polls.Data.TryAdd(name, []))
                        await command.RespondAsync("Poll batch " + name + " created", ephemeral: true);
                    else
                        await command.RespondAsync(
                            "Poll batch " + name + " already exists (use deletebatch to delete it)", ephemeral: true);
                    Polls.Save();
                }
                break;
            case "deletebatch":
                {
                    var name = (string)options[0].Value;
                    if (Polls.Data.Remove(name))
                        await command.RespondAsync("Poll batch " + name + " deleted", ephemeral: true);
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                    Polls.Save();
                }
                break;
            case "showbatch":
                {
                    var name = (string)options[0].Value;
                    if (Polls.Data.TryGetValue(name, out var list))
                    {
                        await command.RespondAsync($"Polls in group {name}:\n{PollEntry.ToString(list)}",
                            ephemeral: true);
                    }
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                }
                break;
            case "listbatch":
                {
                    var msg = string.Join("\n",
                        Polls.Data.Select(kvp => $"{kvp.Key}: {string.Join(", ", kvp.Value.Select(v => v.Question))}"));
                    await command.RespondAsync(msg, ephemeral: true);
                }
                break;
            case "add":
                {
                    var name = (string)options[0].Value;
                    var question = (string)options[1].Value;
                    var choices = (string)options[2].Value;
                    var duration = (long)options[3].Value;
                    var allowMultiselect = (bool)options[4].Value;
                    if (Polls.Data.TryGetValue(name, out var list))
                    {
                        var choicesArr = choices.Split(new[] { ',', '|' },
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        list.Add(new(question, choicesArr, (uint)duration, allowMultiselect));
                        Polls.Save();
                        await command.RespondAsync($"Poll added. Polls in group {name}:\n{PollEntry.ToString(list)}",
                            ephemeral: true);
                    }
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                }
                break;
            case "insert":
                {
                    var name = (string)options[0].Value;
                    var index = (long)options[1].Value;
                    var question = (string)options[2].Value;
                    var choices = (string)options[3].Value;
                    var duration = (long)options[4].Value;
                    var allowMultiselect = (bool)options[5].Value;
                    if (Polls.Data.TryGetValue(name, out var list))
                    {
                        if (index < 0 || index > list.Count)
                        {
                            await command.RespondAsync("Poll index out of range of list", ephemeral: true);
                        }
                        else
                        {
                            var choicesArr = choices.Split(new[] { ',', '|' },
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            list.Insert((int)index, new(question, choicesArr, (uint)duration, allowMultiselect));
                            Polls.Save();
                            await command.RespondAsync(
                                $"Poll inserted. Polls in group {name}:\n{PollEntry.ToString(list)}",
                                ephemeral: true);
                        }
                    }
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                }
                break;
            case "edit":
                {
                    var name = (string)options[0].Value;
                    var index = (long)options[1].Value;
                    var question = (string)options[2].Value;
                    var choices = (string)options[3].Value;
                    var duration = (long)options[4].Value;
                    var allowMultiselect = (bool)options[5].Value;
                    if (Polls.Data.TryGetValue(name, out var list))
                    {
                        if (index < 0 || index >= list.Count)
                        {
                            await command.RespondAsync("Poll index out of range of list", ephemeral: true);
                        }
                        else
                        {
                            var choicesArr = choices.Split(new[] { ',', '|' },
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            list[(int)index] = new(question, choicesArr, (uint)duration, allowMultiselect);
                            Polls.Save();
                            await command.RespondAsync(
                                $"Poll edited. Polls in group {name}:\n{PollEntry.ToString(list)}",
                                ephemeral: true);
                        }
                    }
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                }
                break;
            case "remove":
                {
                    var name = (string)options[0].Value;
                    var index = (long)options[1].Value;
                    if (Polls.Data.TryGetValue(name, out var list))
                    {
                        if (index < 0 || index >= list.Count)
                        {
                            await command.RespondAsync("Poll index out of range of list", ephemeral: true);
                        }
                        else
                        {
                            list.RemoveAt((int)index);
                            Polls.Save();
                            await command.RespondAsync(
                                $"Poll removed. Polls in group {name}:\n{PollEntry.ToString(list)}",
                                ephemeral: true);
                        }
                    }
                    else
                        await command.RespondAsync("Poll batch " + name + " doesn't exist", ephemeral: true);
                }
                break;
        }
    }
}
