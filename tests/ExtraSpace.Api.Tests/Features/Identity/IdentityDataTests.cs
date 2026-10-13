using System.Security.Cryptography;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class IdentityDataTests(ApiFactory api)
{
    [Fact]
    public async Task Customer_email_is_unique()
    {
        var email = IdentityTestData.NewEmail();
        await api.CreateCustomerAsync(email);
        var duplicate = () => api.CreateCustomerAsync(email);
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Refresh_tokens_and_login_codes_round_trip_with_utc_times()
    {
        var customer = await api.CreateCustomerAsync();
        var codeEmail = IdentityTestData.NewEmail();
        var expires = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await api.WithDbAsync(async db =>
        {
            db.Add(new CustomerRefreshToken { CustomerId = customer.Id, FamilyId = Guid.CreateVersion7(), TokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), ExpiresAt = expires });
            db.Add(new CustomerLoginCode { Email = codeEmail, CodeHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), ExpiresAt = expires, LastSentAt = expires, WindowStartedAt = expires });
            await db.SaveChangesAsync();
        });
        await api.WithDbAsync(async db =>
        {
            var token = await db.Set<CustomerRefreshToken>().SingleAsync(t => t.CustomerId == customer.Id);
            token.ExpiresAt.Should().Be(expires);
            token.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);

            var code = await db.Set<CustomerLoginCode>().SingleAsync(c => c.Email == codeEmail);
            foreach (var instant in new[] { code.ExpiresAt, code.LastSentAt, code.WindowStartedAt })
            {
                instant.Should().Be(expires);
                instant.Kind.Should().Be(DateTimeKind.Utc);
            }
            code.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        });
    }
}
