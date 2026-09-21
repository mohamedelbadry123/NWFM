using Microsoft.AspNetCore.Identity;

namespace Auth.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid? TeamId { get; set; }
    public string? EmployeeNumber { get; set; }
    public string? DisplayNameAr { get; set; }
}
