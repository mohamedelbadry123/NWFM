using Auth.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Results;

namespace Auth.Infrastructure.Services.Messaging;

public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<Result<bool>> SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        logger.LogInformation("SMS to {PhoneNumber}: {Message}", phoneNumber, message);
        return Task.FromResult(Result<bool>.Success(true));
    }
}
