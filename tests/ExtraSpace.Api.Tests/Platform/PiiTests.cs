using ExtraSpace.Api.Platform.Logging;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class PiiTests
{
    [Theory]
    [InlineData("jane@gmail.com", "j***@gmail.com")]
    [InlineData("j@x.io", "j***@x.io")]
    [InlineData("  Jane@Gmail.com ", "J***@Gmail.com")]
    [InlineData("a@b@c.com", "a***@c.com")]
    [InlineData("no-at-sign", "***")]
    [InlineData("@x.com", "***")]
    [InlineData("x@", "***")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    public void Mask_email(string? input, string expected) => Pii.MaskEmail(input).Should().Be(expected);
}
