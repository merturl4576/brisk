using System.Collections.Generic;

namespace BriskEngine.Models;

public sealed record CleanupTarget(
    string Id,
    string DisplayName,
    CleanupLevel Level,
    IReadOnlyList<string> PathTemplates,
    string Category,
    bool DeletesContentsNotDirectory = false,
    bool Regenerates = false,
    string? RequiresAppClosedProcess = null,
    bool RequiresIndividualSelection = false,
    bool RequiresExplicitOptIn = false,
    bool BypassesRecycleBin = false,
    bool RequiresElevation = false);
