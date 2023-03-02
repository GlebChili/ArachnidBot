using System;
using System.Text;
using System.Reactive;
using System.Linq;
using TL;
using Discord;
using Discord.WebSocket;

namespace ArachnidBot;

public class TelegramObserver
{
    private readonly WTelegram.Client _telegram;
    private readonly IConfiguration _config;
    private readonly Dictionary<long, User> _users;
    private readonly Dictionary<long, ChatBase> _chats;
    private readonly DiscordSocketClient _discord;

    public TelegramObserver(WTelegram.Client telegram, IConfiguration config,
                            Dictionary<long, User> users, Dictionary<long, ChatBase> chats,
                            DiscordSocketClient discord)
    {
        _telegram = telegram;
        _config = config;
        _users = users;
        _chats = chats;
        _discord = discord;
    }

    public async Task<Unit> NewUserMessageResponder(UpdateNewMessage nm)
    {
        long senderId = nm.message.Peer.ID;
        User? sender = null;

        StaticHelpers.UpdatesMutex.WaitOne();
        bool isUser = _users.ContainsKey(senderId);
        if (isUser)
        {
            sender = _users[senderId];
        }
        ChatBase targetChat = _chats[_config.GetTargetChatId()];
        StaticHelpers.UpdatesMutex.ReleaseMutex();

        if (isUser)
        {
            if (nm.message is Message message)
            {
                var targetGuild = _discord.GetGuild(_config.GetTargetDiscordGuild());
                var targetRole = targetGuild.Roles.First(r => r.Id == _config.GetTargetDiscordRole());

                if (message.message == "/start")
                {
                    string helpText = $"Если вы состоите в Telegram чате {targetChat.Title}, "
                    + $"введите свой Discord ник (в формате Nick#1234), чтобы получить роль " +
                    $"{targetRole.Name} на сервере {targetGuild.Name}.";

                    await _telegram.SendMessageAsync(sender, helpText);

                    return Unit.Default;
                }

                await _telegram.SendMessageAsync(sender, "Проверяю...");

                Dictionary<long, User> chatUsers;

                if (targetChat is Channel targetChannel)
                {
                    chatUsers = (await _telegram.Channels_GetAllParticipants(targetChannel)).users;
                }
                else
                {
                    chatUsers = (await _telegram.GetFullChat(targetChat)).users;
                }

                if (!chatUsers.ContainsKey(senderId))
                {
                    await _telegram.SendMessageAsync(sender, "Похоже вы не состоите в закрытом Telegram чате :(");
                    return Unit.Default;
                }

                await targetGuild.DownloadUsersAsync();
                var guildUsers = targetGuild.Users;

                if (!guildUsers.Any(u => (u.Username + "#" + u.Discriminator) == message.message))
                {
                    await _telegram.SendMessageAsync(sender, "Похоже вас нет на Discord сервере или " +
                        "вы неправильно написали свой ник :(");

                    return Unit.Default;
                }

                IGuildUser targetUser = 
                    guildUsers.First(u => (u.Username + "#" + u.Discriminator) == message.message);

                await targetUser.AddRoleAsync(targetRole); 

                await _telegram.SendMessageAsync(sender, "Роль добавлена!");
            }
        }

        return Unit.Default;
    }
}