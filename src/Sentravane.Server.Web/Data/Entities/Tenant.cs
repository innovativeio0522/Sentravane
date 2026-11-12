using System.ComponentModel.DataAnnotations;

namespace Sentravane.Server.Web.Data.Entities;

public class Tenant
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string Name { get; set; } = string.Empty;

    public string EnrollmentToken { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime EnrollmentTokenExpiresUtc { get; set; } = DateTime.UtcNow.AddDays(30);

    public int MaxSeats { get; set; } = 50;

    public int UsedSeats { get; set; } = 0;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Endpoint> Endpoints { get; set; } = [];
}

