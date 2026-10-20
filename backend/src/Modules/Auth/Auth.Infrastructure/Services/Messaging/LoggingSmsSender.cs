using Auth.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Results;

namespace Auth.Infrastructure.Services.Messaging;

public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<Result<bool>> SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        logger.LogInformation("SMS delivery requested using the local logging transport; message content and recipient are suppressed.");
        return Task.FromResult(Result<bool>.Success(true));
    }
}
