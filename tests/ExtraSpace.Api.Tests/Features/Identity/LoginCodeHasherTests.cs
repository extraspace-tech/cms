using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Features.Identity.Contracts;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class LoginCodeHasherTests(ApiFactory api)
{
    [Fact]
    public void The_purpose_is_part_of_the_hash()
    {
        var hasher = api.Services.GetRequiredService<LoginCodeHasher>();
        const string email = "hash@example.test";
        const string code = "123456";

        var customer = hasher.Hash(LoginCodePurpose.Customer, email, code);
        var staff = hasher.Hash(LoginCodePurpose.Staff, email, code);

        customer.Should().NotBe(staff);
        hasher.Matches(customer, LoginCodePurpose.Customer, email, code).Should().BeTrue();
        hasher.Matches(staff, LoginCodePurpose.Staff, email, code).Should().BeTrue();
        hasher.Matches(customer, LoginCodePurpose.Staff, email, code).Should().BeFalse();
        hasher.Matches(staff, LoginCodePurpose.Customer, email, code).Should().BeFalse();
    }
}
