using System.ComponentModel.DataAnnotations;

namespace InstaTalk.API.Domain.DTOs;

public record CreatePostRequest(
    [Required][MaxLength(500, ErrorMessage = "Post is too long.")] string Content, string? ImageUrl
);

public record UpdatePostRequest(
    [Required][MaxLength(500, ErrorMessage = "Post is too long.")] string Content
);

public record CreateCommentRequest(string Content);

public record CommentResponse(Guid Id, Guid UserId, string Content, DateTime CreatedAt);
