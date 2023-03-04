using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Reactive;
using TL;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace ArachnidBot;

public class KeeperObserver
{
    private readonly WTelegram.Client _telegram;
    private readonly DiscordSocketClient _discord;
    private readonly ConcurrentDictionary<long, ChatBase> _chats;
    private readonly ILogger<KeeperObserver> _logger;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;

    public KeeperObserver(WTelegram.Client telegram, DiscordSocketClient discord,
                          ConcurrentDictionary<long, ChatBase> chats,
                          ILogger<KeeperObserver> logger, IServiceProvider services,
                          IConfiguration config)
    {
        _telegram = telegram;
        _discord = discord;
        _chats = chats;
        _logger = logger;
        _services = services;
        _config = config;
    }

    public async Task<Unit> TelegramChatSynchronize(Unit _)
    {
        _logger.LogDebug("TelegramChatSynchronize was started");

        using var scope = _services.CreateScope();

        var dbcontext = scope.ServiceProvider.GetRequiredService<ArachnidContext>();

        ChatBase targetChat = _chats[_config.GetTargetChatId()];

        Dictionary<long, User> chatUsers;

        if (targetChat is Channel targetChannel)
        {
            chatUsers = (await _telegram.Channels_GetAllParticipants(targetChannel)).users;
        }
        else
        {
            chatUsers = (await _telegram.GetFullChat(targetChat)).users;
        }

        List<UserAssociation> usersToRemove = new();

        await foreach (var ua in dbcontext.UserAssociations.AsAsyncEnumerable())
        {
            if (!chatUsers.ContainsKey(ua.UserTelegramId))
            {
                usersToRemove.Add(ua);

                _logger.LogInformation("User {User} (id {Id}) is mo longer member of the target chat. " + 
                                       "Removed from database. Their Discord user was {DisName} (id {DisId})",
                                       ua.TelegramName, ua.UserDiscordId, ua.DiscordName, ua.UserDiscordId);
            }
        }

        dbcontext.UserAssociations.RemoveRange(usersToRemove);

        await dbcontext.SaveChangesAsync();

        _logger.LogDebug("TelegramChatSynchronize completed successfully");

        return Unit.Default;
    }

    public async Task<Unit> DiscordRolesSynchronize(Unit _)
    {
        _logger.LogDebug("DiscordRolesSynchronize was started");

        using var scope = _services.CreateScope();

        var dbcontext = scope.ServiceProvider.GetRequiredService<ArachnidContext>();

        SocketGuild targetGuild = _discord.GetGuild(_config.GetTargetDiscordGuild());

        SocketRole targetRole = targetGuild.GetRole(_config.GetTargetDiscordRole());

        var roleMembers = targetRole.Members.ToList();

        foreach (var m in roleMembers)
        {
            if (!await dbcontext.UserAssociations.AnyAsync(ua => ua.UserDiscordId == m.Id))
            {
                await m.RemoveRoleAsync(targetRole);

                _logger.LogInformation("The target role for Discord user {User}#{Disc} (id {Id}) was removed, " +
                                       "since they are no longer in association table",
                                       m.Username, m.Discriminator, m.Id);
            }
        }

        _logger.LogDebug("DiscordRolesSynchronize completed successfully");

        return Unit.Default;
    } 
}