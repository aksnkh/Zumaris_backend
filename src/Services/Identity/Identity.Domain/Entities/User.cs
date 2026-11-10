using System;

namespace Zumaris.Identity.Domain.Entities;

public class User
{
    public string Application { get; set; } = "App";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public DateTime? ProviderEnrollmentRequestedAtUtc { get; set; }
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
