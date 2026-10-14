namespace NWFM.Tests.Modules.Auth;

using FluentAssertions;
using global::Auth.Domain.Constants;
using global::Auth.Domain.Entities;
using NWFM.Shared.Exceptions;

/// <summary>A coverage row as the teams and users screens save it: a place, departments, task types, or a mix.</summary>
public sealed class OrgScopeTests
{
    private static readonly string Owner = OrgScopeOwnerTypes.TeamOwnerId(Guid.NewGuid());

    [Fact]
    public void Create_ARowNarrowedToTaskTypesAlone_IsAccepted()
    {
        var leaks = Guid.NewGuid();

        var scope = OrgScope.Create(OrgScopeOwnerTypes.Team, Owner, null, null, taskTypeIds: [leaks]);

        scope.HasTerritory.Should().BeFalse();
        scope.Departments.Should().BeEmpty();
        scope.TaskTypes.Select(t => t.TaskTypeId).Should().Equal(leaks);
        scope.TaskTypes.Should().OnlyContain(t => t.OrgScopeId == scope.Id);
    }

    [Fact]
    public void Create_DropsEmptyAndRepeatedTaskTypes()
    {
        var leaks = Guid.NewGuid();
        var isolation = Guid.NewGuid();

        var scope = OrgScope.Create(
            OrgScopeOwnerTypes.User, Owner, OrgScopeLevels.Branch, "R-16", ["10"], [leaks, Guid.Empty, isolation, leaks]);

        scope.TaskTypes.Select(t => t.TaskTypeId).Should().Equal(leaks, isolation);
    }

    [Fact]
    public void Create_ARowNamingNothing_IsRefused()
    {
        var create = () => OrgScope.Create(OrgScopeOwnerTypes.Team, Owner, null, null, [], [Guid.Empty]);

        create.Should().Throw<DomainException>().WithMessage("*task type*");
    }
}
