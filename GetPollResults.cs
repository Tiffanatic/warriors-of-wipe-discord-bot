using System.Text;
using Discord;
using Discord.WebSocket;

namespace WarriorsOfWipeBot;

public class GetPollResults
{
    public static readonly ApplicationCommandProperties[] Commands =
    [
        new SlashCommandBuilder()
            .WithName("getpollresults")
            .WithDescription("Get poll results into text form")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .Build()
    ];

    public GetPollResults(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += SlashCommandExecuted;
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName is not "omegapoll")
            return;

        // might take a while to run
        await command.DeferAsync(true);

        var channel = await command.GetChannelAsync();
        StringBuilder msg = new();
        await foreach (var messageBatch in channel.GetMessagesAsync())
        {
            foreach (var message in messageBatch)
            {
                if (message is SocketUserMessage userMessage && userMessage.Poll.HasValue &&
                    userMessage.Poll.Value.Results.HasValue)
                {
                    var poll = userMessage.Poll.Value;
                    msg.AppendLine(poll.Question.Text);

                    foreach (var option in poll.Answers)
                    {
                        msg.Append(option.PollMedia.Text + ": ");
                        var first = true;
                        await foreach (var userArray in userMessage.GetPollAnswerVotersAsync(option.AnswerId))
                        {
                            foreach (var user in userArray)
                            {
                                if (first)
                                    first = false;
                                else
                                    msg.Append(", ");
                                msg.Append(user.Mention);
                            }
                        }

                        msg.AppendLine();
                    }
                }
            }
        }

        await command.FollowupAsync(msg.ToString(), ephemeral: true);
    }
}
