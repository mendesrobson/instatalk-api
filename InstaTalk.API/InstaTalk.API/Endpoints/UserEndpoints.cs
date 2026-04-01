

using global::InstaTalk.API.Domain.Entities;
using global::InstaTalk.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InstaTalk.API.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").RequireAuthorization();

        group.MapGet("/search", SearchUsers);
        group.MapPost("/{id:guid}/follow", ToggleFollow);
    }

    // 1. Rota para procurar usuários pelo e-mail
    private static async Task<IResult> SearchUsers(
        string q, // O termo de busca que virá da URL (?q=termo)
        ClaimsPrincipal user,
        AppDbContext db)
    {
        var currentUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (string.IsNullOrWhiteSpace(q))
            return Results.Ok(new List<object>());

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.Email.Contains(q) && u.Id != currentUserId) // Não traz a si mesmo
            .Select(u => new
            {
                u.Id,
                u.Email,
                // O banco já verifica se o usuário logado segue essa pessoa
                IsFollowing = db.UserFollows.Any(f => f.FollowerId == currentUserId && f.FollowedId == u.Id)
            })
            .Take(20) // Proteção contra paginação infinita
            .ToListAsync();

        return Results.Ok(users);
    }

    // 2. Rota para Seguir / Dar Unfollow
    private static async Task<IResult> ToggleFollow(
        Guid id, // ID do usuário alvo
        ClaimsPrincipal user,
        AppDbContext db)
    {
        var currentUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (currentUserId == id)
            return Results.BadRequest(new { error = "Você não pode seguir a si mesmo." });

        var targetUserExists = await db.Users.AnyAsync(u => u.Id == id);
        if (!targetUserExists)
            return Results.NotFound(new { error = "Usuário não encontrado." });

        // Verifica se já segue
        var existingFollow = await db.UserFollows
            .FirstOrDefaultAsync(f => f.FollowerId == currentUserId && f.FollowedId == id);

        if (existingFollow != null)
        {
            // Unfollow
            db.UserFollows.Remove(existingFollow);
            await db.SaveChangesAsync();
            return Results.Ok(new { action = "unfollowed" });
        }
        else
        {
            // Follow
            var follow = new UserFollow
            {
                FollowerId = currentUserId,
                FollowedId = id
            };
            db.UserFollows.Add(follow);
            await db.SaveChangesAsync();
            return Results.Ok(new { action = "followed" });
        }
    }
}
