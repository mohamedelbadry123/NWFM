using System.Reflection;
using System.Text.Json;
using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent cluster / CBU / branch / department / operation-area seed, ported from the
/// reference app <c>FsmsSeedData.SeedReferenceDataAsync</c>.
/// </summary>
internal static class AuthLookupSeedData
{
    private const string BranchSeedResourceName = "Auth.Infrastructure.Persistence.Seed.AuthLookupBranchSeed.json";

    private static readonly JsonSerializerOptions SeedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static async Task SeedAsync(AuthDbContext context, CancellationToken ct = default)
    {
        await UpsertDepartmentsAsync(context, ct);
        await UpsertClustersAsync(context, ct);
        await UpsertCbusAsync(context, ct);
        await context.SaveChangesAsync(ct);

        await UpsertBranchesAsync(context, ct);
        await context.SaveChangesAsync(ct);

        await UpsertOperationAreasAsync(context, ct);
        await context.SaveChangesAsync(ct);
    }

    private static async Task UpsertDepartmentsAsync(AuthDbContext context, CancellationToken ct)
    {
        (string Code, string NameEn, string NameAr)[] departments =
        [
            ("10", "Water Network", "شبكة المياه"),
            ("11", "Waste Water", "شبكة الصرف الصحي"),
            ("50", "New Connections", "إدارة التوصيلات المنزلية")
        ];

        var existing = await context.Departments.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (code, nameEn, nameAr) in departments)
        {
            if (existing.TryGetValue(code, out var current))
            {
                current.Update(nameEn, nameAr);
                current.SetActive(true);
                continue;
            }

            context.Departments.Add(Department.Create(code, nameEn, nameAr));
        }
    }

    private static async Task UpsertClustersAsync(AuthDbContext context, CancellationToken ct)
    {
        (string Code, string NameEn, string NameAr)[] clusters =
        [
            ("CC", "Central Cluster", "القطاع الأوسط"),
            ("WC", "West Cluster", "القطاع الغربى"),
            ("EC", "East Cluster", "القطاع الشرقى"),
            ("SC", "South Cluster", "القطاع الجنوبى"),
            ("NC", "North Cluster", "القطاع الشمالى"),
            ("NWC", "North West Cluster", "القطاع الشمالى الغربى")
        ];

        var existing = await context.Clusters.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (code, nameEn, nameAr) in clusters)
        {
            if (existing.TryGetValue(code, out var current))
            {
                current.Update(nameEn, nameAr);
                current.SetActive(true);
                continue;
            }

            context.Clusters.Add(Cluster.Create(code, nameEn, nameAr));
        }
    }

    private static async Task UpsertCbusAsync(AuthDbContext context, CancellationToken ct)
    {
        (string Code, string ClusterCode, string NameEn, string NameAr)[] cbus =
        [
            ("RCBU", "CC", "RIYADH City", "مدينة الرياض"),
            ("RI", "CC", "RIYADH Directorate", "منطقة الرياض"),
            ("JCBU", "WC", "JEDDAH City", "مدينة جده"),
            ("MCBU", "WC", "Makkah City", "مدينة مكه"),
            ("TCBU", "WC", "TAIF City", "مدينة الطائف"),
            ("MK", "WC", "MAKKA Directorate", "منطقة مكة"),
            ("SH", "EC", "EASTERN Directorate", "منطقة الشرقية"),
            ("AS", "SC", "ASEER Directorate", "منطقة عسير"),
            ("BA", "SC", "BAHA Directorate", "منطقة الباحة"),
            ("NJ", "SC", "NAJRAN Directorate", "منطقة نجران"),
            ("JZBU", "SC", "JIZAN Directorate", "منطقة جازان"),
            ("HA", "NC", "HAIL Directorate", "منطقة حايل"),
            ("HS", "NC", "Al Hudud Al Shamaliyah Directorate", "منطقة الحدود الشمالية"),
            ("JF", "NC", "JOUF Directorate", "منطقة الجوف"),
            ("QS", "NC", "ALQASEEM Directorate", "منطقة القصيم"),
            ("MD", "NWC", "MEDINA Directorate", "منطقة المدينة المنورة"),
            ("TB", "NWC", "TABUK City", "مدينة تبوك"),
            ("HQ", "CC", "Head Office", "المركز الرئيسي")
        ];

        var existing = await context.Cbus.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (code, clusterCode, nameEn, nameAr) in cbus)
        {
            if (existing.TryGetValue(code, out var current))
            {
                current.Update(nameEn, nameAr, clusterCode);
                current.SetActive(true);
                continue;
            }

            context.Cbus.Add(Cbu.Create(code, nameEn, nameAr, clusterCode));
        }
    }

    private static async Task UpsertBranchesAsync(AuthDbContext context, CancellationToken ct)
    {
        (string Code, string CbuCode, string BranchCode, string NameEn, string NameAr)[] aliases =
        [
            ("1110", "RCBU", "R-16", "Al Moraba-Riyadh", "فرع المربع - الرياض"),
            ("2200", "JCBU", "J-01", "JCBU Main Back Office", "المكتب الخلفي الرئيسي - جدة")
        ];

        foreach (var z in aliases)
        {
            var existing = await context.Branches.FirstOrDefaultAsync(x => x.Code == z.Code, ct);
            if (existing is null)
            {
                context.Branches.Add(Branch.Create(z.Code, z.NameEn, z.NameAr, z.CbuCode, z.BranchCode));
                continue;
            }

            existing.Update(z.NameEn, z.NameAr, z.CbuCode);
            existing.SetActive(true);
        }

        var rows = ReadEmbeddedSeed<BranchSeedRow>(BranchSeedResourceName);
        var knownCbus = new HashSet<string>(
            await context.Cbus.AsNoTracking().Select(x => x.Code).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);
        var existingBranches = await context.Branches.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var row in rows)
        {
            if (!knownCbus.Contains(row.CbuCode))
                continue;

            if (existingBranches.TryGetValue(row.Code, out var current))
            {
                current.Update(row.NameEn, row.NameAr, row.CbuCode);
                current.SetActive(true);
                continue;
            }

            context.Branches.Add(Branch.Create(row.Code, row.NameEn, row.NameAr, row.CbuCode, row.Code));
        }
    }

    private static async Task UpsertOperationAreasAsync(AuthDbContext context, CancellationToken ct)
    {
        (string Code, string CbuCode, string MainAreaCode, string NameEn, string NameAr)[] operationAreas =
        [
            ("MA6", "RCBU", "MA6", "Riyadh Operation Area 6", "منطقة العمليات ٦ - الرياض"),
            ("RC-01", "RCBU", "MA6", "Al Moraba Operation Area", "منطقة عمليات المربع"),
            ("JISO", "JCBU", "JISO", "Jeddah ISO Operation Area", "منطقة عمليات جدة - أيزو")
        ];

        foreach (var area in operationAreas)
        {
            var existing = await context.OperationAreas
                .FirstOrDefaultAsync(x => x.CbuCode == area.CbuCode && x.Code == area.Code, ct);
            if (existing is not null)
            {
                existing.Update(area.NameEn, area.NameAr, area.CbuCode);
                existing.SetActive(true);
                continue;
            }

            context.OperationAreas.Add(OperationArea.Create(area.Code, area.NameEn, area.NameAr, area.CbuCode, area.MainAreaCode));
        }
    }

    private static IReadOnlyList<T> ReadEmbeddedSeed<T>(string resourceName)
    {
        var assembly = typeof(AuthLookupSeedData).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded seed resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<List<T>>(json, SeedJsonOptions)
            ?? throw new InvalidOperationException($"Embedded seed resource '{resourceName}' is empty.");
    }

    private sealed record BranchSeedRow(string Code, string CbuCode, string NameEn, string NameAr);
}
