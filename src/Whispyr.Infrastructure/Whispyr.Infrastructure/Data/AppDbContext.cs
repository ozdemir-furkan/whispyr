using Microsoft.EntityFrameworkCore;
using Whispyr.Domain.Entities;

namespace Whispyr.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RoomSummary> RoomSummaries => Set<RoomSummary>();

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

    // Room
    modelBuilder.Entity<Room>(e =>
    {
        e.HasKey(r => r.Id);
        e.Property(r => r.Code).IsRequired();
        e.HasIndex(r => r.Code).IsUnique();

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
    });

    // RoomSummary
    modelBuilder.Entity<RoomSummary>(e =>
    {
       e.HasKey(x => x.Id);
       e.Property(x => x.Content).IsRequired();
       e.Property(x => x.CreatedAt).IsRequired();
       e.HasIndex(x => new { x.RoomId, x.Id });
    });

    modelBuilder.Entity<Room>(e =>
    {
    e.HasKey(r => r.Id);
    e.Property(r => r.Code).IsRequired();
    e.HasIndex(r => r.Code).IsUnique();

    e.Property(r => r.Title)
     .IsRequired()
     .HasMaxLength(80);            // <— max length

    e.Property(r => r.UpdatedAt);   // <— opsiyonel ama şema için ek

    e.HasOne(r => r.Owner)
     .WithMany(u => u.Rooms)
     .HasForeignKey(r => r.OwnerId)
     .OnDelete(DeleteBehavior.SetNull);
    });


    }
}
