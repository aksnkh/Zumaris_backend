namespace Identity.Application.Features.Auth.Models;

// Internal service command: the controller supplies the application from its route.
public sealed record LoginRequest(string Email, string Password, string Application);
