using System.ComponentModel.DataAnnotations;

namespace Sentravane.Server.Web.Data.Entities;

public class User
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Foreign key to Tenant. Null indicates a platform-level SuperAdmin.
    /// </summary>
    public string? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// "SuperAdmin" or "TenantAdmin"
    /// </summary>
    [Required]
    public string Role { get; set; } = "TenantAdmin";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAtUtc { get; set; }
}
