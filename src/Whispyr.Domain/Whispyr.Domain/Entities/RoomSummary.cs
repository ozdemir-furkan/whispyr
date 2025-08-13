namespace Whispyr.Domain.Entities;

public class RoomSummary
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}