using Discord;
using Discord.WebSocket;

namespace WarriorsOfWipeBot;

[Serializable]
internal class LfgDeleteEntry(ulong channelId, ulong messageId, long deleteAtTime)
{
    public ulong ChannelId = channelId;
    public ulong MessageId = messageId;
    public long DeleteAtTime = deleteAtTime;
}

public class NoLfg
{
    private const ulong ModLogsChannel = 1211482792328831017UL;
    private const ulong LfgChannel = 1209317207490957362UL;
    private const ulong LfgChatChannel = 1211466357669896284UL;
    private const ulong ModsRole = 1208599020134600734UL;
    private const ulong MentorRole = 1208606814770565131UL;

    private readonly DiscordSocketClient client;
    private readonly Json<List<LfgDeleteEntry>> LfgDelete = new("lfgdelete.json");

    public NoLfg(DiscordSocketClient client)
    {
        this.client = client;
        client.MessageReceived += OnMessageReceived;
        client.MessageDeleted += OnMessageDeleted;
        client.LatencyUpdated += TickUpdate;
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        if (msg.Channel.Id != LfgChannel)
            return;

        var author = msg.Author;
        // bots and webhooks are allowed to post whatever
        if (author.IsBot || author.IsWebhook)
            return;

        // mods and mentors are allowed to post polls that last a long time
        if (msg is SocketUserMessage { Poll: not null } && author is SocketGuildUser u2 &&
            u2.Roles.Any(r => r.Id is MentorRole or ModsRole))
            return;

        // PF posts last for an hour, including if posted by mods or mentors
        if (msg.Attachments.Count > 0 || msg.Embeds.Count > 0)
        {
            AddToTimedDelete(msg);
            return;
        }

        // if a mod posts a non PF post, it's allowed to exist forever
        if (author is SocketGuildUser user && user.Roles.Any(r => r.Id == ModsRole))
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

    private void AddToTimedDelete(SocketMessage msg)
    {
        var now = DateTimeOffset.UtcNow;
        var time = now + TimeSpan.FromMinutes(70);
        LfgDelete.Data.Add(new LfgDeleteEntry(msg.Channel.Id, msg.Id, time.ToUnixTimeSeconds()));
        LfgDelete.Save();
    }

    private Task OnMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        var numRemoved = LfgDelete.Data.RemoveAll(e => e.MessageId == message.Id);
        if (numRemoved > 0)
            LfgDelete.Save();
        return Task.CompletedTask;
    }

    private async Task TickUpdate(int o, int n)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var msgDeleteChanged = false;
        for (var i = LfgDelete.Data.Count - 1; i >= 0; i--)
        {
            var entry = LfgDelete.Data[i];
            if (now < entry.DeleteAtTime)
                continue;

            LfgDelete.Data.RemoveAt(i);
            msgDeleteChanged = true;

            if (await client.GetChannelAsync(entry.ChannelId) is IMessageChannel ch &&
                await ch.GetMessageAsync(entry.MessageId) is { } msg)
            {
                await msg.DeleteAsync();
                var failed = false;
                try
                {
                    await msg.Author.SendMessageAsync(
                        $"You left a PF post up in {MentionUtils.MentionChannel(entry.ChannelId)} for longer than an hour. Please delete it when the PF has filled! (If your PF still hasn't filled, feel free to repost it now)");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to send lfg delete DM:");
                    Console.WriteLine(e);
                    failed = true;
                }

                if (ch is SocketGuildChannel channel && channel.Guild.GetTextChannel(ModLogsChannel) is
                        { } modLogs)
                {
                    await modLogs.SendMessageAsync(
                        $"{msg.Author.Mention} did not delete their PF post in {MentionUtils.MentionChannel(msg.Channel.Id)}{(failed ? " (failed to send DM)" : "")}");
                }
            }
        }

        if (msgDeleteChanged)
            LfgDelete.Save();
    }
}