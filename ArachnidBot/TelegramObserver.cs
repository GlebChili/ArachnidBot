using System;
using System.Text;
using System.Reactive;
using System.Linq;
using System.Collections.Concurrent;
using TL;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace ArachnidBot;

public class TelegramObserver
{
    private readonly WTelegram.Client _telegram;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<long, ChatBase> _chats;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger<TelegramObserver> _logger;
    private readonly IServiceProvider _services;

    public TelegramObserver(WTelegram.Client telegram, IConfiguration config,
                            ConcurrentDictionary<long, ChatBase> chats,
                            DiscordSocketClient discord,
                            ILogger<TelegramObserver> logger,
                            IServiceProvider services)
    {
        _telegram = telegram;
        _config = config;
        _chats = chats;
        _discord = discord;
        _logger = logger;
        _services = services;
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
                    $"{targetRole.Name} на сервере {targetGuild.Name}. " + 
                    "Вы должны быть on-line в Discord, чтобы бот вас мог увидеть.";

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

                SocketUser? checkUser;

                try
                {
                    var split = message.message.Split("#");
                    checkUser = _discord.GetUser(split[0], split[1]);
                }
                catch
                {
                    checkUser = null;
                }

                if (checkUser is null || targetGuild.GetUser(checkUser.Id) is null)
                {
                    await _telegram.SendMessageAsync(sender, "Похоже вас нет на Discord сервере или " +
                        "вы неправильно написали свой ник :(");

                    _logger.LogInformation("User {User} (id {Id}) has provided non-existing or invalid " +
                                           "Discord Guild username", sender!.MainUsername, sender!.ID);

                    return Unit.Default;
                }

                IGuildUser targetUser = targetGuild.GetUser(checkUser.Id);

                using var scope = _services.CreateScope();
                var dbcontext = scope.ServiceProvider.GetRequiredService<ArachnidContext>();

                if (await dbcontext.UserAssociations.AnyAsync(ua => ua.UserDiscordId == targetUser.Id))
                {
                    await _telegram.SendMessageAsync(sender, 
                          $"Похоже пользователь {targetUser.Username}#{targetUser.Discriminator} " +
                          $"уже имеет Discord-роль {targetRole.Name}");

                    _logger.LogInformation("User {User} (id {Id}) asked to add the role to " +
                                           "Discord user {DisUser}#{Discr}, but the user has the target role already",
                                           sender!.MainUsername, sender!.ID, targetUser.Username, targetUser.Discriminator);

                    return Unit.Default;
                }

                if (await dbcontext.UserAssociations.AnyAsync(ua => ua.UserTelegramId == sender.ID))
                {
                    UserAssociation old = await dbcontext.UserAssociations.SingleAsync
                                                (ua => ua.UserTelegramId == sender.ID);
                    
                    dbcontext.UserAssociations.Remove(old);

                    var oldDisUser = targetGuild.GetUser(old.UserDiscordId);
                    if (oldDisUser is not null)
                    {
                        await oldDisUser.RemoveRoleAsync(targetRole);
                    }

                    UserAssociation fresh = new()
                    {
                        UserTelegramId = old.UserTelegramId,
                        UserDiscordId = targetUser.Id,
                        TimeStamp = DateTime.UtcNow,
                        TelegramName = sender!.MainUsername,
                        DiscordName = $"{targetUser.Username}#{targetUser.Discriminator}"
                    };

                    await dbcontext.UserAssociations.AddAsync(fresh);

                    await targetUser.AddRoleAsync(targetRole);

                    await dbcontext.SaveChangesAsync();

                    _logger.LogInformation("User {User} (id {Id}) requested change of his Discord user " +
                                           "from {oldDisName} (id {oldId}) to " +
                                           "{newDisName} (id {newId})",
                                           sender!.MainUsername, sender!.ID, old.DiscordName, old.UserDiscordId,
                                           fresh.DiscordName, fresh.UserDiscordId);

                    await _telegram.SendMessageAsync(sender, 
                                    $"Ваш Discord пользователь изменен с {old.DiscordName} на {fresh.DiscordName}!");

                    return Unit.Default;
                }
                else
                {
                    UserAssociation fresh = new()
                    {
                        UserTelegramId = sender!.ID,
                        UserDiscordId = targetUser.Id,
                        TimeStamp = DateTime.UtcNow,
                        TelegramName = sender!.MainUsername,
                        DiscordName = $"{targetUser.Username}#{targetUser.Discriminator}"
                    };

                    await dbcontext.UserAssociations.AddAsync(fresh);

                    await targetUser.AddRoleAsync(targetRole);

                    await dbcontext.SaveChangesAsync();

                    _logger.LogInformation("User {User} (id {Id}) added association with Discord user " +
                                           "{disName} (id {disId})",
                                           sender!.MainUsername, sender!.ID, fresh.DiscordName, fresh.UserDiscordId);

                    await _telegram.SendMessageAsync(sender, $"Роль успешно добавлена!");

                    return Unit.Default;
                }
            }
        }

        return Unit.Default;
    }
}