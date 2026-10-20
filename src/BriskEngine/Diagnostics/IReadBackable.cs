using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Diagnostics;

/// A switch brisk can re-read after writing it: whether it still reads as on,
/// and what brisk knows about whether this machine acts on the write.
public interface IReadBackable : IDiagnosticRule
{
    bool IsOn(DiagnosticContext ctx);
    WriteEffect EffectOfTheWrite(DiagnosticContext ctx);
}
