using System;
using System.Text;
using System.Reactive;
using System.Reactive.Subjects;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading;
using System.Reactive.Threading.Tasks;
using System.Reactive.Disposables;
using System.Collections.Concurrent;
using TL;

namespace ArachnidBot;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private readonly WTelegram.Client _telegram;
    private readonly Subject<(Update update, Dictionary<long, User> users)> _updateSubject;
    private readonly ConcurrentDictionary<long, ChatBase> _chats;
    private readonly IObservable<(Update update, Dictionary<long, User> users)> _updates;
    private readonly TelegramObserver _telegaObserver;
    private readonly Discord.WebSocket.DiscordSocketClient _discord;
    private readonly CompositeDisposable _disposables;

    public Worker(ILogger<Worker> logger, IConfiguration config, WTelegram.Client telegram,
                  ConcurrentDictionary<long, ChatBase> chats,
                  TelegramObserver telegaObserver, Discord.WebSocket.DiscordSocketClient discord)
    {
        _logger = logger;
        _config = config;
        _telegram = telegram;
        _updateSubject = new();
        _chats = chats;
        _updates = _updateSubject.ObserveOn(TaskPoolScheduler.Default);
        _telegaObserver = telegaObserver;
        _discord = discord;
        _disposables = new();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting main worker...");

        _logger.LogInformation("Checking Discord client...");

        var disGuild = _discord.GetGuild(_config.GetTargetDiscordGuild());

        if (disGuild is null)
        {
            throw new Exception("Unable to connect to Discord Guild");
        }

        _logger.LogInformation("Logged into Guild {GuildName}:{GuildId} as {BotName}#{Discr}", disGuild.Name,
                               disGuild.Id, disGuild.CurrentUser.DisplayName,
                               disGuild.CurrentUser.Discriminator);

        if (!disGuild.Roles.Any(r => r.Id == _config.GetTargetDiscordRole()))
        {
            throw new Exception("Target Discord Guild does not exists");
        }

        var targetRole = disGuild.Roles.First(r => r.Id == _config.GetTargetDiscordRole());

        _logger.LogInformation("Target Discord Guild Role is {RoleName}", targetRole.Name);
        
        _logger.LogInformation("Checking Telegram client...");
        
        if (_telegram.User.IsBot)
        {
            _logger.LogInformation("Connected as bot {Id}, name {Name}", _telegram.User.ID, _telegram.User.MainUsername);
        }
        else
        {
            throw new Exception("Connected to Telegram as user, not bot");
        }

        _logger.LogInformation("Creating updates observable...");

        _telegram.OnUpdate += arg =>
        {
            if (arg is UpdatesBase updates)
            {
                Dictionary<long, User> users = new();
                Dictionary<long, ChatBase> chats = new();

                updates.CollectUsersChats(users, chats);

                foreach (var p in chats)
                {
                    _chats[p.Key] = p.Value;
                }

                foreach (Update u in updates.UpdateList)
                {
                    _updateSubject.OnNext((u, users));
                }
            }

            return Task.CompletedTask;
        };

        _logger.LogInformation("Updates observable created");

        _logger.LogInformation("Awaiting to register target chat. " + 
                               "To register chat, post any message to it.");

        while (true)
        {
            bool isChatRegistered = _chats.ContainsKey(_config.GetTargetChatId());

            if (isChatRegistered)
            {
                break;
            }

            await Task.Delay(100);
        }
        
        _logger.LogInformation("Target chat registered. Target chat name: {ChatName}",
                               _chats[_config.GetTargetChatId()].Title);

        _logger.LogInformation("Subscribing Telegram updates observers...");

        _updates.Where(t => t.update is UpdateNewMessage)
                .Select<(Update update, Dictionary<long, User> users), 
                        (UpdateNewMessage update, Dictionary<long, User>)>
                        (t => ((t.update as UpdateNewMessage)!, t.users))
                .Select(x => Observable.FromAsync(async () => await _telegaObserver.NewUserMessageResponder(x)))
                .Merge()
                .Do(_ => {}, e => _logger.LogCritical("Critical error while processing telegram message: {E}", e))
                .Retry()
                .Subscribe()
                .DisposeWith(_disposables);

        _logger.LogInformation("Telegram updates observers are registered");

        _logger.LogInformation("Worker started");
        await Task.Delay(-1, stoppingToken);
    }

    public override void Dispose()
    {
        _disposables.Dispose();

        GC.SuppressFinalize(this);

        base.Dispose();
    }
}
