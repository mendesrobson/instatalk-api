namespace InstaTalk.API.Domain.DTOs;

// O campo "Website" é o nosso Honeypot invisível no frontend.
public record RegisterRequest(string Email, string Password, string? Website);
public record LoginRequest(string Email, string Password);
public record TokenResponse(string AccessToken, string RefreshToken);
public record RefreshTokenRequest(string RefreshToken, string Email);