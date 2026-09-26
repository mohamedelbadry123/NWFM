namespace Auth.Application.Auth.Models;

public sealed class SsoStatusDto
{
    public bool Enabled { get; init; }
    public bool AllowLocalLoginForAdministrators { get; init; }
}
