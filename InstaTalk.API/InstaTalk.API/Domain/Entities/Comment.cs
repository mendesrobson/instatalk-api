namespace InstaTalk.API.Domain.Entities;

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Relacionamentos
    public Guid PostId { get; set; }
    public Post? Post { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }
}
