using System.ComponentModel.DataAnnotations;

namespace Identity.Application.Features.Auth.Models;

public sealed record LoginRequest(
    [Required, StringLength(255)] string UserName,
    [Required, StringLength(1024)] string Password);
