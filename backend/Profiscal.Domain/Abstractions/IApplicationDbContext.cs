namespace Profiscal.Domain.Abstractions;

public interface IApplicationDbContext
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
