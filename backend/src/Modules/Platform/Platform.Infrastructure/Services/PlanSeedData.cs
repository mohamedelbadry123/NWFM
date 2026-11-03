
namespace Platform.Infrastructure.Services;

/// <summary>The plans an empty catalog starts with. Edited afterwards from the platform area, never re-seeded.</summary>
internal static class PlanSeedData
{
    public const string Trial = "trial";
    public const string Standard = "standard";
    public const string Enterprise = "enterprise";

    public static IReadOnlyList<Plan> Create() =>
    [
        Plan.Create(Trial, "Trial", "تجريبي",
            "Try every feature with a small team.", "جرّب جميع الميزات مع فريق صغير.",
            maxUsers: 10, maxTeams: 5, maxStorageGb: 5, trialDays: 14, isPublic: true, sortOrder: 1),
        Plan.Create(Standard, "Standard", "قياسي",
            "For growing businesses.", "للأعمال المتنامية.",
            maxUsers: 50, maxTeams: 25, maxStorageGb: 50, trialDays: 0, isPublic: true, sortOrder: 2),
        Plan.Create(Enterprise, "Enterprise", "مؤسسي",
            "Unlimited users, teams and storage.", "مستخدمون وفرق وتخزين بلا حدود.",
            maxUsers: null, maxTeams: null, maxStorageGb: null, trialDays: 0, isPublic: false, sortOrder: 3)
    ];
}
