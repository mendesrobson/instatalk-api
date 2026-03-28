namespace InstaTalk.API.Domain.Entities;

public class Post
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid OwnerId { get; init; }
    public required string Content { get; set; }
    public string? ImageUrl { get; set; } 
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<Like> Likes { get; set; } = new List<Like>();
}