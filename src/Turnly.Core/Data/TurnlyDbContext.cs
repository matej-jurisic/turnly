using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Turnly.Core.Entities;

namespace Turnly.Core.Data;

public class TurnlyDbContext : DbContext
{
    public TurnlyDbContext(DbContextOptions<TurnlyDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Chore> Chores => Set<Chore>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ChoreCompletion> ChoreCompletions => Set<ChoreCompletion>();
    public DbSet<ChoreAssignment> ChoreAssignments => Set<ChoreAssignment>();
    public DbSet<PointsLogEntry> PointsLog => Set<PointsLogEntry>();
    public DbSet<Award> Awards => Set<Award>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<ChoreNotification> ChoreNotifications => Set<ChoreNotification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).IsRequired().HasMaxLength(64);
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(128);
            e.Property(x => x.AvatarColor).IsRequired().HasMaxLength(32);
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);

            e.HasMany(x => x.RefreshTokens)
                .WithOne(x => x.User!)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.PointsLog)
                .WithOne(x => x.User!)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        });

        b.Entity<Chore>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(1024);
            e.Property(x => x.Emoji).HasMaxLength(16);
            e.Property(x => x.RepeatType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CustomMode).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.IntervalUnit).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.FrequencyPeriod).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.AssignmentStrategy).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.SchedulingPreference).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Weekdays).HasConversion(WeekdaysConverter, WeekdaysComparer);
            e.Property(x => x.DaysOfMonth).HasConversion(IntListConverter, IntListComparer);
            e.Property(x => x.Months).HasConversion(IntListConverter, IntListComparer);

            e.HasOne(x => x.CurrentAssignee)
                .WithMany()
                .HasForeignKey(x => x.CurrentAssigneeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Assignees).WithMany();
            e.HasMany(x => x.Tags).WithMany(t => t.Chores);

            e.HasMany(x => x.Completions)
                .WithOne(x => x.Chore!)
                .HasForeignKey(x => x.ChoreId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Assignments)
                .WithOne(x => x.Chore!)
                .HasForeignKey(x => x.ChoreId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Notifications)
                .WithOne(x => x.Chore!)
                .HasForeignKey(x => x.ChoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Tag>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(64);
        });

        b.Entity<ChoreCompletion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Notes).HasMaxLength(1024);

            e.HasOne(x => x.CompletedBy)
                .WithMany()
                .HasForeignKey(x => x.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ChoreAssignment>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PointsLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(256);
        });

        b.Entity<Award>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(1024);
            e.Property(x => x.Emoji).HasMaxLength(16);

            // Deleting an award keeps its redemption history (snapshotted on the redemption).
            e.HasMany(x => x.Redemptions)
                .WithOne(x => x.Award!)
                .HasForeignKey(x => x.AwardId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Redemption>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AwardName).IsRequired().HasMaxLength(128);
            e.Property(x => x.AwardEmoji).HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ChoreNotification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Timing).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.OffsetUnit).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Recipients).HasConversion<string>().HasMaxLength(16);
        });

        b.Entity<PushSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.Property(x => x.Endpoint).IsRequired().HasMaxLength(2048);
            e.Property(x => x.P256dh).IsRequired().HasMaxLength(256);
            e.Property(x => x.Auth).IsRequired().HasMaxLength(256);
            e.Property(x => x.DeviceLabel).HasMaxLength(128);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NotificationDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            // Each schedule entry fires at most once per occurrence.
            e.HasIndex(x => new { x.ChoreNotificationId, x.OccurrenceDueAt }).IsUnique();

            e.HasOne(x => x.ChoreNotification)
                .WithMany()
                .HasForeignKey(x => x.ChoreNotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserNotification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(256);
            e.Property(x => x.Body).IsRequired().HasMaxLength(512);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Keep the inbox record if the chore is deleted; just drop the deep-link.
            e.HasOne(x => x.Chore)
                .WithMany()
                .HasForeignKey(x => x.ChoreId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>Stores the selected weekdays as a comma-separated list of <see cref="DayOfWeek"/> values.</summary>
    private static readonly ValueConverter<List<DayOfWeek>, string> WeekdaysConverter = new(
        v => string.Join(',', v.Select(d => (int)d)),
        v => string.IsNullOrEmpty(v)
            ? new List<DayOfWeek>()
            : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => (DayOfWeek)int.Parse(s)).ToList());

    private static readonly ValueComparer<List<DayOfWeek>> WeekdaysComparer = new(
        (a, b) => (a ?? new List<DayOfWeek>()).SequenceEqual(b ?? new List<DayOfWeek>()),
        v => v.Aggregate(0, (hash, d) => HashCode.Combine(hash, (int)d)),
        v => v.ToList());

    /// <summary>Stores an int list (days of month / months) as a comma-separated string.</summary>
    private static readonly ValueConverter<List<int>, string> IntListConverter = new(
        v => string.Join(',', v),
        v => string.IsNullOrEmpty(v)
            ? new List<int>()
            : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList());

    private static readonly ValueComparer<List<int>> IntListComparer = new(
        (a, b) => (a ?? new List<int>()).SequenceEqual(b ?? new List<int>()),
        v => v.Aggregate(0, HashCode.Combine),
        v => v.ToList());
}
