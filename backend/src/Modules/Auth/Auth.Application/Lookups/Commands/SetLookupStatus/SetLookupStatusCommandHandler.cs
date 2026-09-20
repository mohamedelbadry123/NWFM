using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Lookups.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Commands.SetLookupStatus;

public sealed class SetLookupStatusCommandHandler(IAuthDbContext context)
    : IRequestHandler<SetLookupStatusCommand, Result<LookupItemDto>>
{
    public async Task<Result<LookupItemDto>> Handle(SetLookupStatusCommand request, CancellationToken ct)
    {
        var type = request.LookupType.Trim();
        Result<LookupItemDto> updated = type switch
        {
            "Department" => await SetDepartment(request, ct),
            "Cluster" => await SetCluster(request, ct),
            "Cbu" => await SetCbu(request, ct),
            "Branch" => await SetBranch(request, ct),
            "OperationArea" => await SetOperationArea(request, ct),
            _ => Result<LookupItemDto>.Failure(AuthErrors.LookupUnknownType(type))
        };

        if (updated.IsFailure)
            return updated;

        await context.SaveChangesAsync(ct);
        return updated;
    }

    private async Task<Result<LookupItemDto>> SetDepartment(SetLookupStatusCommand request, CancellationToken ct)
    {
        var entity = await context.Departments.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.SetActive(request.IsActive);
        return new LookupItemDto { Id = entity.Id, Code = entity.Code, NameEn = entity.NameEn, NameAr = entity.NameAr, IsActive = entity.IsActive };
    }

    private async Task<Result<LookupItemDto>> SetCluster(SetLookupStatusCommand request, CancellationToken ct)
    {
        var entity = await context.Clusters.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.SetActive(request.IsActive);
        return new LookupItemDto { Id = entity.Id, Code = entity.Code, NameEn = entity.NameEn, NameAr = entity.NameAr, IsActive = entity.IsActive };
    }

    private async Task<Result<LookupItemDto>> SetCbu(SetLookupStatusCommand request, CancellationToken ct)
    {
        var entity = await context.Cbus.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.SetActive(request.IsActive);
        return new LookupItemDto { Id = entity.Id, Code = entity.Code, NameEn = entity.NameEn, NameAr = entity.NameAr, IsActive = entity.IsActive, ParentCode = entity.ClusterCode };
    }

    private async Task<Result<LookupItemDto>> SetBranch(SetLookupStatusCommand request, CancellationToken ct)
    {
        var entity = await context.Branches.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.SetActive(request.IsActive);
        return new LookupItemDto { Id = entity.Id, Code = entity.Code, NameEn = entity.NameEn, NameAr = entity.NameAr, IsActive = entity.IsActive, ParentCode = entity.CbuCode };
    }

    private async Task<Result<LookupItemDto>> SetOperationArea(SetLookupStatusCommand request, CancellationToken ct)
    {
        var entity = await context.OperationAreas.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.SetActive(request.IsActive);
        return new LookupItemDto { Id = entity.Id, Code = entity.Code, NameEn = entity.NameEn, NameAr = entity.NameAr, IsActive = entity.IsActive, ParentCode = entity.CbuCode };
    }
}
