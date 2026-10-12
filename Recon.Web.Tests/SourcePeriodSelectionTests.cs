using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class SourcePeriodSelectionTests
{
    [Fact]
    public void Matching_source_months_correct_a_mistaken_selection()
    {
        var result = SourcePeriodSelection.Correction("2025-04", "2025-04", new(2026, 3, 1), new(2026, 3, 31));
        Assert.Equal((new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)), result!.Value);
    }
    [Fact]
    public void Deliberate_partial_month_selection_is_preserved() =>
        Assert.Null(SourcePeriodSelection.Correction("2025-04", "2025-04", new(2025, 4, 1), new(2025, 4, 15)));
    [Theory]
    [InlineData(null, "2025-04")] [InlineData("2025-04", null)] [InlineData("2025-04", "2025-03")]
    public void Unknown_or_different_source_months_are_not_guessed(string? processor, string? gl) =>
        Assert.Null(SourcePeriodSelection.Correction(processor, gl, new(2026, 3, 1), new(2026, 3, 31)));
}
