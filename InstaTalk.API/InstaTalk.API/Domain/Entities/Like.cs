namespace InstaTalk.API.Domain.Entities;

public class Like
{
    public Guid PostId { get; init; }
    public Guid UserId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
