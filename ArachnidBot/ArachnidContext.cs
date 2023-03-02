using System;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArachnidBot;

public class ArachnidContext : DbContext
{
    private readonly IConfiguration _config;
    private readonly string _dbUrl;

    public required DbSet<UserAssociation> UserAssociations { get; set; }

    public ArachnidContext(IConfiguration config)
    {
        _config = config;
        _dbUrl = _config["DATABASE_URL"]!;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string? host = _config["PGHOST"];
        string? port = _config["PGPORT"];
        string? database = _config["PGDATABASE"];
        string? username = _config["PGUSER"];
        string? password = _config["PGPASSWORD"];

        string connection = $"Host={host};Port={port};Database={database};"
                          + $"Username={username};Password={password}";

        optionsBuilder.UseNpgsql(connection);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAssociation>()
                    .HasKey(c => c.UserTelegramId);
        
        modelBuilder.Entity<UserAssociation>()
                    .HasAlternateKey(c => c.UserDiscordId);
    }
}

public class UserAssociation
{
    public long UserTelegramId { get; set; }
    public ulong UserDiscordId { get; set; }
    public DateTime TimeStamp { get; set; }
}