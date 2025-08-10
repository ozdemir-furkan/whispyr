using Microsoft.EntityFrameworkCore;
using Whispyr.Domain.Entities;

namespace Whispyr.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Room>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Message>().HasIndex(x => x.RoomId);
    }
}
