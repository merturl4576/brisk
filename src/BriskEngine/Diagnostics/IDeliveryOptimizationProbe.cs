namespace BriskEngine.Diagnostics;

/// Delivery Optimization is the part of Windows that uploads content from
/// this machine to other machines. Windows keeps a running count of how much
/// went out; this probe is the only thing in brisk that asks for it.
///
/// The figure is one Windows has already counted, so brisk asks for it
/// rather than measuring anything.
///
/// A REQUIREMENT ON IMPLEMENTORS, not an observation about them: an
/// implementation of this interface opens no connection, and one that
/// measured an upload by performing one would break the promise the whole
/// disclosure wave is built on. RealDeliveryOptimizationProbe is the only
/// implementation today and holds to it; nothing here enforces it on the
/// next one. What Windows does inside its own service to answer is Windows'
/// business, and not something brisk has watched.
public interface IDeliveryOptimizationProbe
{
    /// Bytes this machine uploaded to other machines this month, or null when
    /// the counter cannot be read. null is not zero: a machine that uploaded
    /// nothing and a machine brisk could not ask are different claims.
    long? BytesUploadedToPeers();
}
