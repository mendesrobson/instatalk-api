using InstaTalk.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InstaTalk.API.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Like> Likes => Set<Like>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasKey(u => u.Id);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<Post>().HasKey(p => p.Id);

        // Chave composta para Like: Um utilizador só pode ter um like por post
        modelBuilder.Entity<Like>().HasKey(l => new { l.PostId, l.UserId });

        base.OnModelCreating(modelBuilder);
    }

    // DEFESA EM PROFUNDIDADE: Executa as operações dentro do contexto do Row Level Security
    public async Task<int> SaveChangesWithRlsAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // CORREÇÃO: Usar set_config permite a parametrização segura ($1) que o Npgsql exige.
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