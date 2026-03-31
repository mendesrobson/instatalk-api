namespace InstaTalk.API.Domain.Entities;

public class UserFollow
{
    // Quem está seguindo (O fã)
    public Guid FollowerId { get; set; }
    public User? Follower { get; set; }

    // Quem está sendo seguido (O ídolo)
    public Guid FollowedId { get; set; }
    public User? Followed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
