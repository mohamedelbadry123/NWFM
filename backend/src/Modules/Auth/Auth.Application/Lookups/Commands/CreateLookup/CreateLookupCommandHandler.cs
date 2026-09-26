using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Lookups.Queries;
using Auth.Domain.Entities.Lookups;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Commands.CreateLookup;

public sealed class CreateLookupCommandHandler(IAuthDbContext context, ICacheService cache)
    : IRequestHandler<CreateLookupCommand, Result<LookupItemDto>>
{
    public async Task<Result<LookupItemDto>> Handle(CreateLookupCommand request, CancellationToken ct)
    {
        try
        {
            var type = request.LookupType.Trim();
            var code = request.Code.Trim();
            if (!await LookupParentValidator.IsValidAsync(context, type, request.ParentCode, ct))
                return Result<LookupItemDto>.Failure(new Error("Auth.LookupParentInvalid", "Select an active parent lookup."));

            Result<LookupItemDto> created = type switch
            {
                "Department" => await CreateDepartment(code, request, ct),
                "FieldActivityType" => await CreateFieldActivityType(code, request, ct),
                "Cluster" => await CreateCluster(code, request, ct),
                "Cbu" => await CreateCbu(code, request, ct),
                "Branch" => await CreateBranch(code, request, ct),
                "OperationArea" => await CreateOperationArea(code, request, ct),
                _ => Result<LookupItemDto>.Failure(AuthErrors.LookupUnknownType(type))
            };

            if (created.IsFailure)
                return created;

            await context.SaveChangesAsync(ct);

            // Scopes are expanded through the org hierarchy, so a changed unit must not be read from the old one.
            await cache.RemoveAsync(CacheKeys.Lookups.OrgHierarchy, ct);
            return created;
        }
        catch (DomainException ex)
        {
            return Result<LookupItemDto>.Failure(new Error("Auth.LookupInvalid", ex.Message));
        }
    }

    private async Task<Result<LookupItemDto>> CreateDepartment(string code, CreateLookupCommand request, CancellationToken ct)
    {
        if (await context.Departments.AnyAsync(x => x.Code == code, ct))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupDuplicate);
        var entity = Department.Create(code, request.NameEn, request.NameAr);
        context.Departments.Add(entity);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, null);
    }

    private async Task<Result<LookupItemDto>> CreateCluster(string code, CreateLookupCommand request, CancellationToken ct)
    {
        if (await context.Clusters.AnyAsync(x => x.Code == code, ct))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupDuplicate);
        var entity = Cluster.Create(code, request.NameEn, request.NameAr);
        context.Clusters.Add(entity);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, null);
    }

    private async Task<Result<LookupItemDto>> CreateCbu(string code, CreateLookupCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ParentCode))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupParentRequired);
        if (await context.Cbus.AnyAsync(x => x.Code == code, ct))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupDuplicate);
        var entity = Cbu.Create(code, request.NameEn, request.NameAr, request.ParentCode);
        context.Cbus.Add(entity);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.ClusterCode);
    }

    private async Task<Result<LookupItemDto>> CreateBranch(string code, CreateLookupCommand request, CancellationToken ct)
    {
        if (await context.Branches.AnyAsync(x => x.Code == code, ct))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupDuplicate);
        var entity = Branch.Create(code, request.NameEn, request.NameAr, request.ParentCode, null);
        context.Branches.Add(entity);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.CbuCode);
    }

    private async Task<Result<LookupItemDto>> CreateOperationArea(string code, CreateLookupCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ParentCode))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupParentRequired);
        if (await context.OperationAreas.AnyAsync(x => x.Code == code, ct))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupDuplicate);
        var entity = OperationArea.Create(code, request.NameEn, request.NameAr, request.ParentCode, null);
        context.OperationAreas.Add(entity);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.CbuCode);
    }

    private static LookupItemDto Map(Guid id, string code, string nameEn, string nameAr, bool isActive, string? parent) =>
        new() { Id = id, Code = code, NameEn = nameEn, NameAr = nameAr, IsActive = isActive, ParentCode = parent };

    private async Task<Result<LookupItemDto>> CreateFieldActivityType(string code, CreateLookupCommand request, CancellationToken ct)
    {
        if (await context.FieldActivityTypes.AnyAsync(x => x.Code == code && x.DepartmentCode == request.ParentCode, ct))
            return Result<LookupItemDto>.Failure(AuthErrors.LookupDuplicate);
        var entity = FieldActivityType.Create(code, request.NameEn, request.NameAr, request.ParentCode!);
        context.FieldActivityTypes.Add(entity);
        return Map(entity.Id, entity.Code, entity.NameEn, entity.NameAr, entity.IsActive, entity.DepartmentCode);
    }
}
