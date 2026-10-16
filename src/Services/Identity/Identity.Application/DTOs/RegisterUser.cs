using System.ComponentModel.DataAnnotations;

namespace Zumaris.Identity.Application.DTOs;

public record RegisterRequest(
    [Required, StringLength(255)] string Username,
    [Required, StringLength(1024, MinimumLength = 8)] string Password,
    [Required, EmailAddress, StringLength(254)] string Email);

public record RegisterResponse(bool Success, string Message);
