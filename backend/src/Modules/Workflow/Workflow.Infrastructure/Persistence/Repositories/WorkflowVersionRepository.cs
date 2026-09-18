namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowVersionRepository : IWorkflowVersionRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowVersionRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowVersion?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<WorkflowVersion?> GetByIdWithProjectionAsync(
        Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .Include(v => v.Activities)
                .ThenInclude(a => a.AssignmentRules)
            .Include(v => v.Activities)
                .ThenInclude(a => a.Outcomes)
            .Include(v => v.Activities)
                .ThenInclude(a => a.Actions)
            .Include(v => v.Transitions)
            .Include(v => v.Variables)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<WorkflowVersion?> GetLatestPublishedWithProjectionAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .Include(v => v.Activities)
                .ThenInclude(a => a.AssignmentRules)
            .Include(v => v.Activities)
                .ThenInclude(a => a.Outcomes)
            .Include(v => v.Activities)
                .ThenInclude(a => a.Actions)
            .Include(v => v.Transitions)
            .Include(v => v.Variables)
            .Where(v => v.WorkflowDefinitionId == definitionId
                     && v.Status == Domain.Enums.WorkflowVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkflowVersion?> GetLatestPublishedAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .Where(v => v.WorkflowDefinitionId == definitionId
                     && v.Status == Domain.Enums.WorkflowVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetNextVersionNumberAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
    {
        var max = await _db.WorkflowVersions
            .Where(v => v.WorkflowDefinitionId == definitionId)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken);
        return (max ?? 0) + 1;
    }

    public Task<bool> HasDraftAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .AnyAsync(v => v.WorkflowDefinitionId == definitionId
                        && v.Status == Domain.Enums.WorkflowVersionStatus.Draft,
                cancellationToken);

    public async Task<(IReadOnlyList<WorkflowVersion> Items, int TotalCount)> GetPagedByDefinitionAsync(
        Guid definitionId, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowVersions
            .Where(v => v.WorkflowDefinitionId == definitionId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(v => v.VersionNumber)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowVersion version, CancellationToken cancellationToken = default)
        => await _db.WorkflowVersions.AddAsync(version, cancellationToken);

    public async Task ReplaceProjectionAsync(
        Guid versionId,
        IEnumerable<ActivityDefinition> activities,
        IEnumerable<WorkflowTransition> transitions,
        IEnumerable<WorkflowVariableDefinition> variables,
        IEnumerable<ActivityAssignmentRule> rules,
        IEnumerable<ActivityOutcomeDefinition> outcomes,
        IEnumerable<ActivityActionDefinition> actions,
        CancellationToken cancellationToken = default)
    {
        var existingActivities = await _db.ActivityDefinitions
            .Where(a => a.WorkflowVersionId == versionId)
            .ToListAsync(cancellationToken);

        var existingActivityIds = existingActivities.Select(a => a.Id).ToHashSet();

        var existingRules = await _db.ActivityAssignmentRules
            .Where(r => existingActivityIds.Contains(r.ActivityDefinitionId))
            .ToListAsync(cancellationToken);

        var existingOutcomes = await _db.ActivityOutcomeDefinitions
            .Where(o => existingActivityIds.Contains(o.ActivityDefinitionId))
            .ToListAsync(cancellationToken);

        var existingActions = await _db.ActivityActionDefinitions
            .Where(a => existingActivityIds.Contains(a.ActivityDefinitionId))
            .ToListAsync(cancellationToken);

        var existingTransitions = await _db.WorkflowTransitions
            .Where(t => t.WorkflowVersionId == versionId)
            .ToListAsync(cancellationToken);

        var existingVariables = await _db.WorkflowVariableDefinitions
            .Where(v => v.WorkflowVersionId == versionId)
            .ToListAsync(cancellationToken);

        _db.ActivityAssignmentRules.RemoveRange(existingRules);
        _db.ActivityOutcomeDefinitions.RemoveRange(existingOutcomes);
        _db.ActivityActionDefinitions.RemoveRange(existingActions);
        _db.ActivityDefinitions.RemoveRange(existingActivities);
        _db.WorkflowTransitions.RemoveRange(existingTransitions);
        _db.WorkflowVariableDefinitions.RemoveRange(existingVariables);

        await _db.ActivityDefinitions.AddRangeAsync(activities, cancellationToken);
        await _db.WorkflowTransitions.AddRangeAsync(transitions, cancellationToken);
        await _db.WorkflowVariableDefinitions.AddRangeAsync(variables, cancellationToken);
        await _db.ActivityAssignmentRules.AddRangeAsync(rules, cancellationToken);
        await _db.ActivityOutcomeDefinitions.AddRangeAsync(outcomes, cancellationToken);
        await _db.ActivityActionDefinitions.AddRangeAsync(actions, cancellationToken);
    }

    public async Task<List<string>> GetRequiredAssignmentKeysAsync(
        Guid versionId, CancellationToken cancellationToken = default)
    {
        var activityIds = await _db.ActivityDefinitions
            .Where(a => a.WorkflowVersionId == versionId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        return await _db.ActivityAssignmentRules
            .Where(r => activityIds.Contains(r.ActivityDefinitionId) && r.AssignmentKey != null)
            .Select(r => r.AssignmentKey!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
