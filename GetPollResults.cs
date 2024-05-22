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
        if (command.CommandName is not "getpollresults")
            return;

        var channel = await command.GetChannelAsync();
        StringBuilder msg = new();
        var numMessagesSeen = 0;
        bool anySeen = false;
        await foreach (var messageBatch in channel.GetMessagesAsync())
        {
            anySeen = true;
            foreach (var message in messageBatch)
            {
                numMessagesSeen++;
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

        var msgText = msg.ToString();
        if (string.IsNullOrEmpty(msgText))
        {
            if (numMessagesSeen == 0)
            {
                await command.RespondAsync("No messages were able to be fetched (anySeen=" + anySeen + ")",
                    ephemeral: true);
            }
            else
            {
                await command.RespondAsync("No polls found", ephemeral: true);
            }
        }
        else
        {
            await command.RespondAsync(msg.ToString(), ephemeral: true);
        }
    }
}
