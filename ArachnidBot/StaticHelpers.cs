using System;
using System.Reactive;
using System.Reactive.Disposables;

namespace ArachnidBot;

public static class StaticHelpers
{
    public readonly static Mutex UpdatesMutex;

    static StaticHelpers()
    {
        UpdatesMutex = new();
    }

    public static long GetTargetChatId(this IConfiguration config)
    {
        return long.Parse(config["CHAT_ID"]!);
    }

    public static ulong GetTargetDiscordGuild(this IConfiguration config)
    {
        return ulong.Parse(config["DISCORD_GUILD"]!);
    }

    public static ulong GetTargetDiscordRole(this IConfiguration config)
    {
        return ulong.Parse(config["DISCORD_ROLE"]!);
    }

    public static T DisposeWith<T>(this T item, CompositeDisposable compositeDisposable)
        where T : IDisposable
    {
        if (compositeDisposable is null)
        {
            throw new ArgumentNullException(nameof(compositeDisposable));
        }

        compositeDisposable.Add(item);
        return item;
    }
}
