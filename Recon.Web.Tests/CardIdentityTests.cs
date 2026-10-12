using Recon.Web.Services.Recon;
using Recon.Web.Platform.Adapters;
using Recon.Web.Platform.Files;
using Recon.Web.Domain.Staging;
using Xunit;

namespace Recon.Web.Tests;

public class CardIdentityTests
{
    private sealed class Sink : IStagingSink
    {
        public List<object> Rows { get; } = [];
        public int Count => Rows.Count;
        public void Add(object row) => Rows.Add(row);
    }
    [Fact]
    public void Platform_adapters_accept_customer_underscore_headers_and_store_plain_cards()
    {
        var headings = new[] { "MICROFILM_REF_NUMBER", "INTERCHANGE_FEES", "CARD_ACCOUNT_ENTITY_ID", "CARD_NUMBER", "PRODUCT_TYPE" };
        var file = FileReader.FromCsvLines([
            new FileReader.CsvRow(1, headings),
            new FileReader.CsvRow(2, ["", "0", "4111111111111111", "4111111111111111", "DC"])
        ], new FileReader.CsvRow(1, headings));
        var adapter = new BankOneInterchangeAdapter(); var sink = new Sink();
        Assert.True(adapter.CanDetect(file.Headers));
        Assert.Equal(1, adapter.Parse(file, new ParseContext(1, "2023-03", "synthetic.csv"), sink));
        var row = Assert.IsType<StgInterchangeFee>(Assert.Single(sink.Rows));
        Assert.Equal(CardIdentity.Normalize("4111111111111111"), row.CardAccountEntityId);
        Assert.Equal("4111111111111111", row.CardAccountEntityId);
        var glHeaders = new[] { "SOL_ID", "GL_SUB_HEAD_CODE", "TRAN_RMKS", "TRAN_AMT", "ACCT_NUM" };
        var glFile = FileReader.FromCsvLines([
            new FileReader.CsvRow(1, glHeaders),
            new FileReader.CsvRow(2, ["1", "1", "4111111111111111", "100", "9849065772"])
        ], new FileReader.CsvRow(1, glHeaders));
        var glAdapter = new BankOneGlAdapter(); var glSink = new Sink();
        Assert.True(glAdapter.CanDetect(glFile.Headers));
        Assert.Equal(1, glAdapter.Parse(glFile, new ParseContext(2, "2023-03", "synthetic-gl.csv"), glSink));
        Assert.Equal(row.CardAccountEntityId, Assert.IsType<StgGlEntry>(Assert.Single(glSink.Rows)).TranRmks);
    }
    [Fact]
    public void Formatting_is_normalized_for_direct_plain_comparison()
    {
        var token = CardIdentity.Normalize("4111 1111-1111 1111");
        Assert.Equal(CardIdentity.Normalize("4111111111111111"), token);
        Assert.Equal(token, CardIdentity.Normalize(token));
        Assert.Equal("4111111111111111", token);
        Assert.NotEqual(token, CardIdentity.Normalize("5555555555554444"));
        Assert.Equal("[card number available]", CardIdentity.Display(token));
        Assert.Null(CardIdentity.Normalize("card:v1:" + new string('A', 64)));
    }
    [Theory]
    [InlineData(4111111111111111d)]
    public void Numeric_Excel_card_cells_are_not_trusted(double value)
    {
        Assert.Null(CardIdentity.FromSource(value));
        Assert.Equal("[card identity requires text]", CardIdentity.ImportRemarks(value));
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("****1111")]
    [InlineData("4.11111111111111E+15")] [InlineData("Card 4111111111111111")]
    [InlineData("1111")] [InlineData("41111111111111111111")]
    public void Partial_or_ambiguous_input_is_never_identity_evidence(string? value) => Assert.Null(CardIdentity.Normalize(value));
}
