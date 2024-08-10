using Discord;
using Discord.WebSocket;

namespace WarriorsOfWipeBot;

public class NoLfg
{
    private const ulong ModLogsChannel = 1211482792328831017UL;
    private const ulong LfgChannel = 1209317207490957362UL;
    private const ulong LfgChatChannel = 1211466357669896284UL;
    private const ulong ModsRole = 1208599020134600734UL;
    private const ulong MentorRole = 1208606814770565131UL;

    public NoLfg(DiscordSocketClient client)
    {
        client.MessageReceived += OnMessageReceived;
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        if (msg.Channel.Id != LfgChannel)
        {
            return;
        }

        var author = msg.Author;
        if (author.IsBot || author.IsWebhook || author is SocketGuildUser user && user.Roles.Any(r => r.Id == ModsRole))
            return;

        if (msg.Attachments.Count > 0 || msg.Embeds.Count > 0 || msg is SocketUserMessage { Poll: not null } &&
            author is SocketGuildUser u2 && u2.Roles.Any(r => r.Id == MentorRole))
            return;

        var failed = false;
        try
        {
            await author.SendMessageAsync(
                $"{MentionUtils.MentionChannel(msg.Channel.Id)} is only for posting /raid signups or PF posts. Please use {MentionUtils.MentionChannel(LfgChatChannel)} to chat and organize further.");
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to send NoLfg DM:");
            Console.WriteLine(e);
            failed = true;
        }

        if (msg.Channel is SocketGuildChannel channel && channel.Guild.GetTextChannel(ModLogsChannel) is { } modLogs)
        {
            await modLogs.SendMessageAsync(
                $"{author.Mention} tried to send a message in {MentionUtils.MentionChannel(msg.Channel.Id)}{(failed ? " (failed to send DM)" : "")}: {msg.Content}");
        }

        await msg.DeleteAsync();
    }
}
