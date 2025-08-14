namespace Whispyr.Domain.Entities;

public class Message
{
        public long Id { get; set; }

        public int RoomId { get; set; }
        public Room Room { get; set; } = null!;

        public string? AuthorHash { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsFlagged { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
