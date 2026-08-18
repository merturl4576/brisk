using System;

namespace BriskEngine.Diagnostics;

/// The display driver refused a mode change, or could not save one. It is an
/// exception rather than a return code on purpose: FixRunner turns a throwing
/// Fix into FixOutcome(false, …), so a refused mode reaches the user as a
/// failed fix instead of a success nobody can see on screen.
public sealed class DisplayChangeException : Exception
{
    public DisplayChangeException(string message) : base(message) { }
}
