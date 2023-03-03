using System;
using System.Text;
using System.Reactive;
using System.Linq;
using System.Collections.Concurrent;
using TL;
using Discord;
using Discord.WebSocket;

namespace ArachnidBot;

public class TelegramObserver
{
    private readonly WTelegram.Client _telegram;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<long, ChatBase> _chats;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger<TelegramObserver> _logger;

    public TelegramObserver(WTelegram.Client telegram, IConfiguration config,
                            ConcurrentDictionary<long, ChatBase> chats,
                            DiscordSocketClient discord,
                            ILogger<TelegramObserver> logger)
    {
        _telegram = telegram;
        _config = config;
        _chats = chats;
        _discord = discord;
        _logger = logger;
    }

    public async Task<Unit> NewUserMessageResponder((UpdateNewMessage update, Dictionary<long, User> users) tuple)
    {
        UpdateNewMessage nm = tuple.update;
        Dictionary<long, User> users = tuple.users;

        long senderId = nm.message.Peer.ID;
        User? sender = null;

        bool isUser = users.ContainsKey(senderId);
        if (isUser)
        {
            sender = users[senderId];
        }
        ChatBase targetChat = _chats[_config.GetTargetChatId()];

        if (isUser)
        {
            if (nm.message is Message message)
            {
                _logger.LogInformation("Processing Telegram message \"{Message}\" from user {User} (id {Id})",
                                       message.message, sender!.MainUsername, sender.ID);

                var targetGuild = _discord.GetGuild(_config.GetTargetDiscordGuild());
                var targetRole = targetGuild.Roles.First(r => r.Id == _config.GetTargetDiscordRole());

                if (message.message == "/start")
                {
                    string helpText = $"Если вы состоите в Telegram чате {targetChat.Title}, "
                    + $"введите свой Discord ник (в формате Nick#1234), чтобы получить роль " +
                    $"{targetRole.Name} на сервере {targetGuild.Name}.";

                    await _telegram.SendMessageAsync(sender, helpText);

                    _logger.LogInformation("User {User} (id {Id}) has requested help message",
                                           sender!.MainUsername, sender!.ID);

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
                    
                    _logger.LogInformation("User {User} (id {Id}) is not member of target telegram chat",
                                           sender!.MainUsername, sender.ID);

                    return Unit.Default;
                }

                await targetGuild.DownloadUsersAsync();
                var guildUsers = targetGuild.Users;

                if (!guildUsers.Any(u => (u.Username + "#" + u.Discriminator) == message.message))
                {
                    await _telegram.SendMessageAsync(sender, "Похоже вас нет на Discord сервере или " +
                        "вы неправильно написали свой ник :(");

                    _logger.LogInformation("User {User} (id {Id}) has provided non-existing or invalid " +
                                           "Discord Guild username", sender!.MainUsername, sender!.ID);

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