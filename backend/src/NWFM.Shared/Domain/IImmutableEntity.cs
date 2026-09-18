namespace NWFM.Shared.Domain;

/// <summary>
/// Marker interface for append-only entities (ConsentTransaction, DataSharingTransaction, AuditLog).
/// ImmutableEntityInterceptor blocks any EF Core Modified/Deleted state on these types.
/// Legal basis: PDPL Art. 9, Art. 20, NCA ECC.
/// </summary>
public interface IImmutableEntity { }
