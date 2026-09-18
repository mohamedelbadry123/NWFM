namespace Workflow.Application.Commands.CreateAssignmentGroup;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateAssignmentGroupCommandHandler
    : IRequestHandler<CreateAssignmentGroupCommand, Result<WorkflowAssignmentGroupDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _repo;

    public CreateAssignmentGroupCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowAssignmentGroupDto>> Handle(
        CreateAssignmentGroupCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowAssignmentGroupDto>(gateResult.Error);

        var code = await ResolveUniqueCodeAsync(
            request.OrganizationId,
            request.Code,
            request.Name,
            excludingGroupId: null,
            cancellationToken);

        var now = DateTime.UtcNow;
        var group = WorkflowAssignmentGroup.Create(
            organizationId: request.OrganizationId,
            code: code,
            name: request.Name,
            assignmentStrategy: request.AssignmentStrategy,
            createdAt: now,
            nameAr: request.NameAr);

        await _repo.AddAsync(group, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(group));
    }

    private async Task<string> ResolveUniqueCodeAsync(
        Guid organizationId,
        string? requestedCode,
        string name,
        Guid? excludingGroupId,
        CancellationToken cancellationToken)
    {
        var baseCode = string.IsNullOrWhiteSpace(requestedCode)
            ? AssignmentGroupCodeGenerator.FromName(name)
            : requestedCode.Trim().ToUpperInvariant();

        for (var suffix = 1; suffix <= 99; suffix++)
        {
            var candidate = AssignmentGroupCodeGenerator.WithSuffix(baseCode, suffix);
            var exists = await _repo.CodeExistsAsync(
                candidate, organizationId, excludingGroupId, cancellationToken);
            if (!exists)
                return candidate;
        }

        return AssignmentGroupCodeGenerator.WithSuffix(baseCode, Random.Shared.Next(100, 999));
    }

    private static WorkflowAssignmentGroupDto MapToDto(WorkflowAssignmentGroup g) =>
        new(g.Id, g.OrganizationId, g.Code, g.Name, g.NameAr,
            g.AssignmentStrategy, g.IsActive, MemberCount: 0, g.CreatedAt, g.UpdatedAt);
}
