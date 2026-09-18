using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace NWFM.Shared.Persistence;

/// <summary>
/// Value comparers for JSON-serialized collection properties.
/// Required when using HasConversion on List/Dictionary so EF Core can detect mutations correctly.
/// </summary>
public static class EfCollectionComparers
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    public static ValueComparer<List<T>> List<T>() =>
        new(
            (left, right) =>
                ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            value => value == null
                ? 0
                : value.Aggregate(
                    0,
                    (hash, item) => HashCode.Combine(
                        hash,
                        item == null ? 0 : EqualityComparer<T>.Default.GetHashCode(item))),
            value => value == null ? new List<T>() : value.ToList());

    /// <summary>
    /// Deep comparer for lists of non-equality-aware types (uses JSON snapshotting).
    /// </summary>
    public static ValueComparer<List<T>> JsonList<T>(JsonSerializerOptions? options = null)
    {
        var opts = options ?? DefaultJsonOptions;
        return new ValueComparer<List<T>>(
            (left, right) =>
                ReferenceEquals(left, right)
                || (left != null
                    && right != null
                    && JsonSerializer.Serialize(left, opts) == JsonSerializer.Serialize(right, opts)),
            value => value == null
                ? 0
                : JsonSerializer.Serialize(value, opts).GetHashCode(StringComparison.Ordinal),
            value => value == null
                ? new List<T>()
                : JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(value, opts), opts)
                    ?? new List<T>());
    }

    public static ValueComparer<Dictionary<string, object>?> NullableObjectDictionary(
        JsonSerializerOptions? options = null)
    {
        var opts = options ?? DefaultJsonOptions;
        return new ValueComparer<Dictionary<string, object>?>(
            (left, right) =>
                ReferenceEquals(left, right)
                || (left == null && right == null)
                || (left != null
                    && right != null
                    && JsonSerializer.Serialize(left, opts) == JsonSerializer.Serialize(right, opts)),
            value => value == null
                ? 0
                : JsonSerializer.Serialize(value, opts).GetHashCode(StringComparison.Ordinal),
            value => value == null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(value, opts), opts));
    }
}
