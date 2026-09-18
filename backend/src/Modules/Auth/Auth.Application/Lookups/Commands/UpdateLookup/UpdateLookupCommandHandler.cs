using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Lookups.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Commands.UpdateLookup;

public sealed class UpdateLookupCommandHandler(IAuthDbContext context)
    : IRequestHandler<UpdateLookupCommand, Result<LookupItemDto>>
{
    public async Task<Result<LookupItemDto>> Handle(UpdateLookupCommand request, CancellationToken ct)
    {
        try
        {
            var type = request.LookupType.Trim();
            Result<LookupItemDto> updated = type switch
            {
                "Department" => await UpdateDepartment(request, ct),
                "Cluster" => await UpdateCluster(request, ct),
                "Cbu" => await UpdateCbu(request, ct),
                "Branch" => await UpdateBranch(request, ct),
                "OperationArea" => await UpdateOperationArea(request, ct),
                _ => Result<LookupItemDto>.Failure(AuthErrors.LookupUnknownType(type))
            };

            if (updated.IsFailure)
                return updated;

            await context.SaveChangesAsync(ct);
            return updated;
        }
        catch (DomainException ex)
        {
            return Result<LookupItemDto>.Failure(new Error("Auth.LookupInvalid", ex.Message));
        }
    }

    private async Task<Result<LookupItemDto>> UpdateDepartment(UpdateLookupCommand request, CancellationToken ct)
    {
        var entity = await context.Departments.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.Update(request.NameEn, request.NameAr);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, null);
    }

    private async Task<Result<LookupItemDto>> UpdateCluster(UpdateLookupCommand request, CancellationToken ct)
    {
        var entity = await context.Clusters.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.Update(request.NameEn, request.NameAr);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, null);
    }

    private async Task<Result<LookupItemDto>> UpdateCbu(UpdateLookupCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ParentCode))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupParentRequired);
        var entity = await context.Cbus.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.Update(request.NameEn, request.NameAr, request.ParentCode);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.ClusterCode);
    }

    private async Task<Result<LookupItemDto>> UpdateBranch(UpdateLookupCommand request, CancellationToken ct)
    {
        var entity = await context.Branches.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.Update(request.NameEn, request.NameAr, request.ParentCode);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.CbuCode);
    }

    private async Task<Result<LookupItemDto>> UpdateOperationArea(UpdateLookupCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ParentCode))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupParentRequired);
        var entity = await context.OperationAreas.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (entity is null) return Result<LookupItemDto>.Failure(AuthErrors.LookupNotFound);
        entity.Update(request.NameEn, request.NameAr, request.ParentCode);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.CbuCode);
    }

    private static LookupItemDto Map(Guid id, string code, string nameEn, string nameAr, bool isActive, string? parent) =>
        new() { Id = id, Code = code, NameEn = nameEn, NameAr = nameAr, IsActive = isActive, ParentCode = parent };
}
