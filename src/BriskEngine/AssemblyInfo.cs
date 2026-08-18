using System.Runtime.CompilerServices;

// BriskEngine.Tests needs BootEventParser (internal by design — it is the
// engine's own event-XML parsing, not part of the engine's public surface).
// Exposing it is what lets the "read by field name, never by index" rule be
// proved on any machine, unelevated, without the admin-only channel.
[assembly: InternalsVisibleTo("BriskEngine.Tests")]
