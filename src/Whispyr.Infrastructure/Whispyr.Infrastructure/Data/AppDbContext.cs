using Microsoft.EntityFrameworkCore;
using Whispyr.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Whispyr.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RoomSummary> RoomSummaries => Set<RoomSummary>();

    public override int SaveChanges()
    {
        TouchTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void TouchTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var e in ChangeTracker.Entries<Room>())
        {
            if (e.State == EntityState.Added)
            {
                if (e.Entity.CreatedAt == default) e.Entity.CreatedAt = now;
                e.Entity.UpdatedAt = now;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.UpdatedAt = now;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
        });

        // Room  (TEK blok!)
        modelBuilder.Entity<Room>(e =>
        {
            e.HasKey(r => r.Id);

            e.Property(r => r.Code).IsRequired();
            e.HasIndex(r => r.Code).IsUnique();

            e.Property(r => r.Title)
             .IsRequired()
             .HasMaxLength(80);

            // DB tarafında default (mevcut kolon için migration veya manuel ALTER gerekir)
            e.Property(r => r.UpdatedAt)
             .HasDefaultValueSql("(NOW() AT TIME ZONE 'UTC')");

            e.HasOne(r => r.Owner)
             .WithMany(u => u.Rooms)
             .HasForeignKey(r => r.OwnerId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(r => r.Messages)
             .WithOne(m => m.Room)
             .HasForeignKey(m => m.RoomId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Message
        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Text).IsRequired();
            e.HasIndex(m => m.RoomId);
            // İlişki Room tarafında tanımlı (HasMany/WithOne)
        });

        // RoomSummary
        modelBuilder.Entity<RoomSummary>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => new { x.RoomId, x.Id });
        });
    }
}
