namespace Whispyr.Domain.Entities;

public class Message
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public string AuthorHash { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsFlagged { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Room? Room { get; set; }
}
