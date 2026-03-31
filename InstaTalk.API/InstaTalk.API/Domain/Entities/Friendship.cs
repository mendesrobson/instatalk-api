namespace InstaTalk.API.Domain.Entities;

public enum FriendshipStatus
{
    Pending = 0,   // Convite enviado, aguardando resposta
    Accepted = 1,  // Amigos!
    Declined = 2,  // Convite recusado
    Blocked = 3    // Usuário bloqueado (Opcional para segurança)
}

public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Quem enviou o convite
    public Guid RequesterId { get; set; }
    public User? Requester { get; set; }

    // Quem recebeu o convite
    public Guid AddresseeId { get; set; }
    public User? Addressee { get; set; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
