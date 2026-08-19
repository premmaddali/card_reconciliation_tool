namespace Recon.Web.Domain.Recon;

/// <summary>Outcome tier of a processor↔GL match, derived from the confidence score.</summary>
public enum MatchStatus : byte
{
    Matched = 0,
    AmountMismatch = 1,
    Ambiguous = 2,
    UnmatchedProcessor = 3,
    UnmatchedGl = 4,
    Duplicate = 5
}

/// <summary>Which staging table a match side points at.</summary>
public enum StagingSource : byte
{
    NetworkT464 = 1,
    InterchangeFee = 2,
    DebitSettlementAtm = 3,
    DebitSettlementPos = 4,
    GlEntry = 5
}

public enum ExceptionStatus : byte
{
    Open = 0,
    Investigating = 1,
    Resolved = 2,
    WrittenOff = 3
}
