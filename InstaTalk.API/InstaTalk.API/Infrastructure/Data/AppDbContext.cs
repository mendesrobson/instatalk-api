using InstaTalk.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InstaTalk.API.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments { get; set; }
    public DbSet<UserFollow> UserFollows { get; set; }
    public DbSet<Friendship> Friendships { get; set; }
    public DbSet<Like> Likes => Set<Like>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasKey(u => u.Id);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<Post>().HasKey(p => p.Id);

        // Chave composta para Like: Um utilizador só pode ter um like por post
        modelBuilder.Entity<Like>().HasKey(l => new { l.PostId, l.UserId });

        base.OnModelCreating(modelBuilder);

        // --- Configuração: Comentários ---
        modelBuilder.Entity<Comment>()
            .HasOne(c => c.Post)
            .WithMany(p => p.Comments)
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade); // Se o post sumir, os comentários somem

        modelBuilder.Entity<Comment>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict); // Não apaga o comentário se o usuário for deletado

        // --- Configuração: UserFollow ---
        modelBuilder.Entity<UserFollow>()
            .HasKey(uf => new { uf.FollowerId, uf.FollowedId }); // Chave primária composta (não pode seguir a mesma pessoa 2x)

        modelBuilder.Entity<UserFollow>()
            .HasOne(uf => uf.Follower)
            .WithMany()
            .HasForeignKey(uf => uf.FollowerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserFollow>()
            .HasOne(uf => uf.Followed)
            .WithMany()
            .HasForeignKey(uf => uf.FollowedId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Configuração: Friendship  ---
        modelBuilder.Entity<Friendship>()
            .HasOne(f => f.Requester)
            .WithMany()
            .HasForeignKey(f => f.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Friendship>()
            .HasOne(f => f.Addressee)
            .WithMany()
            .HasForeignKey(f => f.AddresseeId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    // DEFESA EM PROFUNDIDADE: Executa as operações dentro do contexto do Row Level Security
    public async Task<int> SaveChangesWithRlsAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Usar set_config permite a parametrização segura ($1) que o Npgsql exige.
            // O "true" no final indica que a configuração é local para esta transação.
            await Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_user_id', {currentUserId.ToString()}, true)", cancellationToken);

            var result = await base.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}