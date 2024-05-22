using System.Text;
using Discord;
using Discord.Rest;
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

        await command.RespondAsync("Thinking, please wait...", ephemeral: true);

        var channel = await command.GetChannelAsync();
        List<string> msgList = [];
        var numMessagesSeen = 0;
        var anySeen = false;
        await foreach (var messageBatch in channel.GetMessagesAsync())
        {
            anySeen = true;
            foreach (var message in messageBatch)
            {
                numMessagesSeen++;
                if (message is RestUserMessage userMessage && userMessage.Poll.HasValue)
                {
                    var poll = userMessage.Poll.Value;
                    StringBuilder msg = new();
                    msg.AppendLine(poll.Question.Text);

                    foreach (var option in poll.Answers)
                    {
                        msg.Append("### " + option.PollMedia.Text + ": ");
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

                    msgList.Add(msg.ToString());
                }
            }
        }

        msgList.Reverse(); // reverse!!
        var msgText = string.Join("", msgList).Trim();
        if (string.IsNullOrEmpty(msgText))
        {
            if (numMessagesSeen == 0)
            {
                await command.ModifyOriginalResponseAsync(m =>
                    m.Content = "No messages were able to be fetched (anySeen=" + anySeen + ")");
            }
            else
            {
                await command.ModifyOriginalResponseAsync(m => m.Content = "No polls found");
            }
        }
        else
        {
            await command.ModifyOriginalResponseAsync(m => m.Content = msgText);
        }
    }
}
