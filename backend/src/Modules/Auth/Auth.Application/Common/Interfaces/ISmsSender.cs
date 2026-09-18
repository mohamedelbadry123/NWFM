using NWFM.Shared.Results;

namespace Auth.Application.Common.Interfaces;

public interface ISmsSender
{
    Task<Result<bool>> SendAsync(string phoneNumber, string message, CancellationToken ct = default);
}
