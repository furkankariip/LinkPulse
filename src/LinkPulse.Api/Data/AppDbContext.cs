using LinkPulse.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LinkPulse.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> Links => Set<ShortLink>();
    public DbSet<LinkClick> Clicks => Set<LinkClick>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortLink>(link =>
        {
            link.HasIndex(l => l.Code).IsUnique();
            link.Property(l => l.Code).HasMaxLength(32);
            link.Property(l => l.TargetUrl).HasMaxLength(2048);
            link.HasMany(l => l.Clicks)
                .WithOne()
                .HasForeignKey(c => c.ShortLinkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LinkClick>(click =>
        {
            click.HasIndex(c => new { c.ShortLinkId, c.ClickedAt });
            click.Property(c => c.Referrer).HasMaxLength(255);
            click.Property(c => c.Device).HasConversion<string>().HasMaxLength(16);
        });
    }

    // SQLite has no timezone-aware type: values are stored as UTC and read back as UTC.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

public static class DatabaseExtensions
{
    public static async Task EnsureDatabaseCreatedAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
