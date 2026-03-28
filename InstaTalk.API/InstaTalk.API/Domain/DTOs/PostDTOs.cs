using System.ComponentModel.DataAnnotations;

namespace InstaTalk.API.Domain.DTOs;

public record CreatePostRequest(
    [Required][MaxLength(500, ErrorMessage = "Post is too long.")] string Content
);

public record UpdatePostRequest(
    [Required][MaxLength(500, ErrorMessage = "Post is too long.")] string Content
);
