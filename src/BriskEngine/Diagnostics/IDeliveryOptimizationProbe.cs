namespace BriskEngine.Diagnostics;

/// Delivery Optimization is the part of Windows that uploads content from
/// this machine to other machines. Windows keeps a running count of what it
/// uploaded that way, and this probe is what asks for that count.
///
/// The figure is one Windows has already counted, so brisk asks for it
/// rather than measuring anything.
///
/// A REQUIREMENT ON IMPLEMENTORS, not an observation about them: an
/// implementation of this interface opens no connection, and one that
/// measured an upload by performing one would break the promise the whole
/// disclosure wave is built on. The implementations that exist today hold
/// to it — RealDeliveryOptimizationProbe, which shells out to a cmdlet, and
/// the test doubles, which answer from a field — but nothing here can
/// enforce it on the next one. What Windows does inside its own service to
/// answer is Windows' business, and not something brisk has watched.
public interface IDeliveryOptimizationProbe
{
    /// What this machine uploaded to other machines this month, or null when
    /// the counter cannot be read. null is not zero: a machine that uploaded
    /// nothing and a machine brisk could not ask are different claims.
    PeerUpload? UploadedToPeers();
}

/// One month's answer, in Windows' own two halves: bytes that reached
/// machines on this local network, and bytes that reached machines over the
/// internet.
///
/// BOTH HALVES WERE ALWAYS REQUIRED to report at all — a shape brisk only
/// half recognises is a counter brisk did not read, and RealDelivery
/// OptimizationProbe.ParseUploaded refuses a snapshot missing either one.
/// Having required them, the probe used to add them and hand back the sum,
/// so a distinction Windows had already drawn was thrown away by the one
/// read that had seen it. This record keeps the halves and offers the sum
/// beside them.
///
/// Total IS THE SUM AND NOTHING MORE. It is not range-checked here: a half
/// below zero is refused by the parse, and a reading whose halves are
/// individually plausible and whose sum is not — two figures large enough to
/// wrap it negative — is refused by DeliveryOptimizationRule before it
/// reaches a sentence. Constructing this record asserts nothing about the
/// numbers in it; a probe answers with one, and the rule decides what it is
/// worth.
public sealed record PeerUpload(long LanBytes, long InternetBytes)
{
    public long Total => LanBytes + InternetBytes;
}
