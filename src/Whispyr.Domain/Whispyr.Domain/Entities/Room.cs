namespace Whispyr.Domain.Entities;

public class Room
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public int? OwnerId { get; set; }
    public User? Owner { get; set; }
}
