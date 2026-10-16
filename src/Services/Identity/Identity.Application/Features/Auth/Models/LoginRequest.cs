namespace Identity.Application.Features.Auth.Models;

public sealed record LoginRequest(string UserName, string Password);
