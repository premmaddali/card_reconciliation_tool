using Recon.Web.Domain.Common;
using Recon.Web.Domain.Import;

namespace Recon.Web.Domain.Recon;

/// <summary>Every non-matched (or ambiguous) outcome, surfaced for the analyst workflow.</summary>
public class ReconException : IAuditable
{
    public int ReconExceptionId { get; set; }
    public int ReconRunId { get; set; }
    public ReconRun ReconRun { get; set; } = null!;

    public string? ReferenceNumber { get; set; }
    /// <summary>Processor card/account identifier, masked or hashed where that is the available source value.</summary>
    public string? ProcessorAccountNumber { get; set; }
    /// <summary>GL settlement account that was compared to the processor transaction.</summary>
    public string? GlAccountNumber { get; set; }
    public ReconSettlementLeg? SettlementLeg { get; set; }
    public MatchStatus ExceptionType { get; set; }
    public int? Ccy { get; set; }
    public decimal Confidence { get; set; }

    public decimal? ExpectedAmount { get; set; }
    public decimal? ActualAmount { get; set; }
    public decimal? Difference { get; set; }
    public DateOnly? TxnDate { get; set; }
    public string Narrative { get; set; } = string.Empty;

    public ExceptionStatus Status { get; set; } = ExceptionStatus.Open;
    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedDate { get; set; }

    public ICollection<ReconExceptionComment> Comments { get; set; } = [];

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

/// <summary>Analyst note on an exception (HtmlSanitizer-cleaned before persistence).</summary>
public class ReconExceptionComment : IAuditable
{
    public int ReconExceptionCommentId { get; set; }
    public int ReconExceptionId { get; set; }
    public ReconException ReconException { get; set; } = null!;
    public string Body { get; set; } = string.Empty;

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
