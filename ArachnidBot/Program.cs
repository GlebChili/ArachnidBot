using ArachnidBot;
using Serilog;
using Serilog.Extensions.Hosting;
using Serilog.Events;
using Discord;
using Discord.WebSocket;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Collections.Concurrent;
using ReactiveMarbles.ObservableEvents;
using Microsoft.EntityFrameworkCore;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(conf_builder =>
    {
        conf_builder.AddInMemoryCollection(ReadEnvVars());
    })
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<Worker>();
        services.AddSingleton<WTelegram.Client>(s => 
        {
            IConfiguration config = s.GetRequiredService<IConfiguration>();
            int apiId = Int32.Parse(config["API_ID"]!);
            string apiHash = config["API_HASH"]!;
            var logger = s.GetRequiredService<ILogger<WTelegram.Client>>();
            WTelegram.Helpers.Log = (_, message) => logger.LogDebug("WTelegramClient.Log: {Message}", message);
            var client = new WTelegram.Client(apiId, apiHash);
            var clientUser = client.LoginBotIfNeeded(config["TELEGA_TOKEN"]).Result;
            return client;
        });
        services.AddSingleton<ConcurrentDictionary<long, TL.ChatBase>>();
        services.AddSingleton<TelegramObserver>();
        services.AddSingleton<DiscordSocketClient>(s => 
        {
            IConfiguration config = s.GetRequiredService<IConfiguration>();

            DiscordSocketClient client = new DiscordSocketClient(new DiscordSocketConfig() {
                GatewayIntents = GatewayIntents.All
            });
            var isReady = client.Events().Ready.Take(1).ToTask();
            
            client.LoginAsync(TokenType.Bot, config["DISCORD_TOKEN"]).Wait();
            client.StartAsync().Wait();

            isReady.Wait();

            return client;
        });
        services.AddDbContext<ArachnidContext>();
    })
    .UseSerilog((hostContext, services, logConf) =>
    {
        logConf.MinimumLevel.Debug();

        if (hostContext.HostingEnvironment.IsDevelopment())
        {
            logConf.WriteTo.Console(LogEventLevel.Debug);
        }
        else
        {
            logConf.WriteTo.Console(LogEventLevel.Information);
        }
    })
    .Build();

Log.Logger.Information("Applying Entity Framework migrations...");
try
{
    using var scope = host.Services.CreateScope();
    var dbcontext = scope.ServiceProvider.GetRequiredService<ArachnidContext>();
    dbcontext.Database.Migrate();
}
catch (Exception e)
{
    Log.Logger.Fatal("Failed to apply migrations: {Exception}", e);
    return;
}
Log.Logger.Information("EF Migrations were successfull applied");

try
{
    host.Run();
}
catch (Exception e)
{
    Log.Logger.Fatal("host.Run threw error: {Exception}", e);
}

static IEnumerable<KeyValuePair<string, string?>> ReadEnvVars()
{
    var dataBaseUrl = ReadEnv("DATABASE_URL");
    if (dataBaseUrl.Value is not null)
    {
        yield return dataBaseUrl;
    }

    var pghost = ReadEnv("PGHOST");
    if (pghost.Value is not null)
    {
        yield return pghost;
    }

    var pgport = ReadEnv("PGPORT");
    if (pgport.Value is not null)
    {
        yield return pgport;
    }

    var pgdatabase = ReadEnv("PGDATABASE");
    if (pgdatabase.Value is not null)
    {
        yield return pgdatabase;
    }

    var pguser = ReadEnv("PGUSER");
    if (pguser.Value is not null)
    {
        yield return pguser;
    }

    var pgpassword = ReadEnv("PGPASSWORD");
    if (pgpassword.Value is not null)
    {
        yield return pgpassword;
    }

    static KeyValuePair<string, string?> ReadEnv (string envVarName)
    {
        return new KeyValuePair<string, string?>(envVarName ,Environment.GetEnvironmentVariable(envVarName));
    }
}
