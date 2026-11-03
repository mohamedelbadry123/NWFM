using System.Text;
using Auth.Domain.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Identity;

public sealed class JwtSettingsValidator(IHostEnvironment environment) : IValidateOptions<JwtSettings>
{
    private const string PlaceholderKey = "REPLACE_WITH_USER_SECRET_OR_ENV_VAR_min_32_chars";
    private const string DevelopmentKey = "NWFM-dev-signing-key-minimum-32-chars-long!";

    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        var key = options.SigningKey;
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32
            || string.Equals(key.Trim(), PlaceholderKey, StringComparison.Ordinal)
            || (!environment.IsDevelopment() && string.Equals(key.Trim(), DevelopmentKey, StringComparison.Ordinal)))
        {
            return ValidateOptionsResult.Fail(
                "JwtSettings:SigningKey requires an explicit secret of at least 32 UTF-8 bytes. "
                + "The checked-in placeholder is forbidden; the development key is allowed only in Development.");
        }
        return ValidateOptionsResult.Success;
    }
}
