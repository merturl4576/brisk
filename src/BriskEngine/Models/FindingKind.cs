namespace BriskEngine.Models;

/// Problem: something brisk judges wrong and, where it can, fixes.
/// Notice: a measured fact brisk can only report — it never lowers the
/// health score, because a score that punishes unchangeable hardware tells
/// the user to fix what brisk itself says it cannot.
public enum FindingKind { Problem, Notice }
