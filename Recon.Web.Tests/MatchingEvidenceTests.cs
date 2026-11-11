using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class MatchingEvidenceTests
{
    [Theory]
    [InlineData("CR", 103, 3, 100)]
    [InlineData(" cr ", 103, 3, 100)]
    [InlineData("CR", -103, -3, 100)]
    [InlineData("CR", 103, -3, 106)]
    [InlineData("CR", 103, null, 103)]
    [InlineData("CR", 3, 3, 0)]
    [InlineData("DC", 103, 3, 103)]
    public void Billing_less_ISS_applies_only_to_credit(string type, decimal billing, int? iss, decimal expected)
    {
        var value = MatchingEvidence.BillingValue(840, 999, 978, 888, 480, billing, type, iss);
        Assert.Equal(480, value.Ccy);
        Assert.Equal(expected, value.Amount);
    }

    [Fact]
    public void Credit_without_billing_cannot_fall_back_to_transaction_or_settlement()
    {
        var value = MatchingEvidence.BillingValue(840, 999, 978, 888, 480, null, "CR", 3);
        Assert.Null(value.Ccy);
        Assert.Null(MatchingEvidence.BillingAmountForMatching("CR", null, 3));
    }
}
