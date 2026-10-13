using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Identity;

internal sealed class CustomerService(AppDbContext db)
{
    public Task<MeResponse?> GetMeAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Set<Customer>().AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new MeResponse(c.Id, c.Email, c.FirstName, c.LastName, c.Phone))
            .SingleOrDefaultAsync(cancellationToken);
}
