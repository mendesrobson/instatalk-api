using InstaTalk.API.Domain.DTOs;
using InstaTalk.API.Domain.Entities;
using InstaTalk.API.Infrastructure.Data;
using InstaTalk.API.Infrastructure.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Security.Claims;

namespace InstaTalk.API.Endpoints;

public static class PostEndpoints
{
    public static void MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/posts")
                       .WithTags("Posts")
                       .RequireAuthorization() // Todas as rotas exigem JWT válido
                       .RequireRateLimiting("StrictPolicy");

        group.MapPost("/", CreatePost);
        group.MapGet("/", GetFeed);
        group.MapPut("/{id:guid}", UpdatePost);
        group.MapDelete("/{id:guid}", DeletePost);
        group.MapPost("/{id:guid}/like", ToggleLike).RequireRateLimiting("LikesPolicy");
        group.MapGet("/{id:guid}/comments", GetComments);
        group.MapPost("/{id:guid}/comments", AddComment);
    }

    // Helper para extrair o ID do JWT (Segurança Primária)
    private static Guid GetUserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // Adicione IEventBus eventBus nos parâmetros
    private static async Task<IResult> CreatePost(
        [FromBody] CreatePostRequest request,
        ClaimsPrincipal user,
        AppDbContext db,
        IEventBus eventBus) // <-- INJEÇÃO AQUI
    {
        var userId = GetUserId(user);
        var post = new Post
        {
            OwnerId = userId,
            Content = request.Content,
            ImageUrl = request.ImageUrl
        };

        db.Posts.Add(post);
        await db.SaveChangesWithRlsAsync(userId);

        // AQUI: A MÁGICA ASSÍNCRONA ACONTECE (Fire and Forget)
        // O usuário não espera isso processar. A API apenas joga na fila e segue a vida.
        // AQUI: A MÁGICA ASSÍNCRONA ACONTECE (V7 requer await)
        try
        {
            await eventBus.PublishPostCreatedEventAsync(post.Id, userId, post.Content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVISO] Falha ao enviar evento para o RabbitMQ: {ex.Message}");
        }

        return Results.Created($"/api/v1/posts/{post.Id}", new { post.Id, post.ImageUrl });
    }

    private static async Task<IResult> UpdatePost(
        Guid id,
        [FromBody] UpdatePostRequest request,
        ClaimsPrincipal user,
        AppDbContext db)
    {
        var userId = GetUserId(user);

        // Tentativa de carregar o post
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id);

        // REGRA DE SEGURANÇA: Retorna 404 estéril (Não vaza se o post existe mas é de outro)
        if (post == null || post.OwnerId != userId)
            return Results.NotFound(new { error = "Post not found or unauthorized." });

        post.Content = request.Content;
        await db.SaveChangesWithRlsAsync(userId);

        return Results.Ok(new { message = "Post updated." });
    }

    private static async Task<IResult> DeletePost(
        Guid id,
        ClaimsPrincipal user,
        AppDbContext db,
        IWebHostEnvironment env)
    {
        var userId = GetUserId(user);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id);

        if (post == null || post.OwnerId != userId)
            return Results.NotFound(new { error = "Post not found or unauthorized." });

        // 1. Apaga do banco de dados primeiro
        db.Posts.Remove(post);
        await db.SaveChangesWithRlsAsync(userId);

        // 2. Limpa o arquivo físico (se existir)
        if (!string.IsNullOrEmpty(post.ImageUrl))
        {
            // Remove a barra inicial ("/uploads/..." vira "uploads/...") para evitar caminhos absolutos errados
            var relativePath = post.ImageUrl.TrimStart('/');
            var fullPath = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), relativePath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath); // Elimina a imagem do disco
            }
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetFeed(
        ClaimsPrincipal user,
        AppDbContext db)
    {
        var currentUserId = GetUserId(user);

        // Usamos AsNoTracking para máxima performance em consultas de leitura pura.
        // Selecionamos apenas os campos necessários (Defense in Depth contra Data Exposure).

        var feed = await db.Posts
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.OwnerId,
                p.Content,
                p.ImageUrl,
                p.CreatedAt,
                LikesCount = p.Likes.Count,
                HasLiked = p.Likes.Any(l => l.UserId == currentUserId),
                // Contagem de comentários direto no banco
                CommentsCount = p.Comments.Count()
            })
            .ToListAsync();

        return Results.Ok(feed);
    }

    private static async Task<IResult> ToggleLike(
        Guid id,
        ClaimsPrincipal user,
        AppDbContext db,
        IConnectionMultiplexer redis)
    {
        var userId = GetUserId(user);
        var lockKey = $"lock:like:{id}:{userId}";

        // DEFESA CONTRA RACE CONDITIONS: Redis Distributed Lock
        // Tenta adquirir um bloqueio (lock) que dura 3 segundos. Só avança se o lock NÃO existir.
        var lockAcquired = await redis.GetDatabase().StringSetAsync(lockKey, "locked", TimeSpan.FromSeconds(3), When.NotExists);

        if (!lockAcquired)
        {
            // Atacante (ou clique duplo do rato) detetado. Aborta.
            return Results.Conflict(new { error = "Too many concurrent requests." });
        }

        try
        {
            // Verifica se o post existe (mesmo que seja de outro user, podemos curtir)
            if (!await db.Posts.AnyAsync(p => p.Id == id))
                return Results.NotFound(new { error = "Post not found." });

            var existingLike = await db.Likes.FirstOrDefaultAsync(l => l.PostId == id && l.UserId == userId);

            if (existingLike != null)
            {
                // Toggle: Já curtiu, então remove o like
                db.Likes.Remove(existingLike);
            }
            else
            {
                // Toggle: Não curtiu, então adiciona
                db.Likes.Add(new Like { PostId = id, UserId = userId });
            }

            await db.SaveChangesWithRlsAsync(userId);

            return Results.Ok(new { action = existingLike != null ? "unliked" : "liked" });
        }
        finally
        {
            // Liberta o lock ativamente assim que terminar (Get Shit Done)
            await redis.GetDatabase().KeyDeleteAsync(lockKey);
        }
    }
    private static async Task<IResult> GetComments(Guid id, AppDbContext db)
    {
        // Retorna os comentários de um post específico, do mais antigo pro mais novo
        var comments = await db.Comments
            .AsNoTracking()
            .Where(c => c.PostId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentResponse(c.Id, c.UserId, c.Content, c.CreatedAt))
            .ToListAsync();

        return Results.Ok(comments);
    }

    private static async Task<IResult> AddComment(
        Guid id, // ID do post que vem da URL
        [FromBody] CreateCommentRequest request,
        ClaimsPrincipal user,
        AppDbContext db)
    {
        var currentUserId = GetUserId(user);

        // 1. Verifica se o post existe antes de comentar
        var postExists = await db.Posts.AnyAsync(p => p.Id == id);
        if (!postExists)
            return Results.NotFound(new { error = "Post não encontrado." });

        // 2. Cria o comentário
        var comment = new Comment
        {
            PostId = id,
            UserId = currentUserId,
            Content = request.Content
        };

        db.Comments.Add(comment);

        // 3. Salva no banco com nossa blindagem RLS (Row-Level Security)
        await db.SaveChangesWithRlsAsync(currentUserId);

        return Results.Created($"/api/v1/posts/{id}/comments/{comment.Id}",
            new CommentResponse(comment.Id, comment.UserId, comment.Content, comment.CreatedAt));
    }
}
