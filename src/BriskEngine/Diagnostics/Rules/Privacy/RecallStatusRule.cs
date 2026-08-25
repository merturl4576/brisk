using System;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// What the policy value that switches Recall's data analysis off says on
/// this machine. brisk reads that value and reports how it read — nothing
/// else. It does not report whether Recall is running here, or whether this
/// build has Recall at all, because reading a policy establishes neither.
///
/// REPORT ONLY, DELIBERATELY, and this is the rule where that decision costs
/// something visible: brisk could write this value as easily as it writes the
/// six switches beside it. It does not, because the surface is new and
/// differs between builds, and a fix brisk cannot check afterwards is the one
/// thing this project refuses to ship. So the rule is Advise, which is the
/// consent level FixRunner declines to apply a fix for at all.
///
/// Three findings, and the test between them is what the value read as: the
/// number that switches it off, the number that leaves it on, and anything
/// brisk did not read as either of those. That third arm is reported as not
/// established — a real answer, and the one the machine this was written on
/// gave. How common it is elsewhere is not something one machine measures, so
/// nothing here says. It is never rounded down to "off": not being able to
/// read a switch is not the switch being off, and that is this wave's
/// standing rule applied to the one setting where the wrong answer would be
/// the reassuring one.
///
/// The path below is UNVERIFIED against a machine that has the policy — this
/// one does not, which is why the third arm is what it reports here. Both
/// constants come from the task brief and only the fake has ever matched
/// them. If brisk never reports either readable state on a machine where
/// Windows shows Recall as switched off, these two are the first things to
/// doubt.
public sealed class RecallStatusRule : PrivacyDisclosureRule
{
    public const string KeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI";
    public const string ValueName = "DisableAIDataAnalysis";

    /// The value's own sense is inverted — it names what it DISABLES — so the
    /// number that reads as protection is the one that switches the analysis
    /// off, and the constants say which is which rather than leaving two bare
    /// literals in a switch.
    private const int SwitchesItOff = 1;
    private const int LeavesItOn = 0;

    public override string Id => "recall-status";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx) => Policy(ctx) switch
    {
        SwitchesItOff => Disclosure(
            $"rule.{Id}.title.off",
            "Recall's data analysis is switched off by policy on this machine",
            $"rule.{Id}.evidence.off",
            Sentence("it is set to switch it off"),
            evidenceArgs: null,
            headline: State("off", "Off")),

        LeavesItOn => Disclosure(
            $"rule.{Id}.title.on",
            "Recall's data analysis is not switched off by policy on this machine",
            $"rule.{Id}.evidence.on",
            Sentence("it is set to leave it on"),
            evidenceArgs: null,
            headline: State("on", "Allowed")),

        // No headline: a headline is what a finding leads with, and what
        // brisk has to lead with here is that it could not tell.
        _ => Disclosure(
            $"rule.{Id}.title.unread",
            "Recall's data analysis state could not be established",
            $"rule.{Id}.evidence.unread",
            "brisk looked for the policy value Windows uses to switch Recall's " +
            "data analysis off and found nothing there it can read as set either " +
            "way. Not being able to read that value is not the same as the " +
            "setting being switched off, and brisk does not report it as off."),
    };

    /// The two readable sentences differ in one clause and agree on the rest,
    /// so the rest is written once: brisk read the policy, brisk reads only
    /// the policy, and brisk is not offering to change this one.
    private static string Sentence(string howItRead) =>
        "brisk read the policy value Windows uses to switch Recall's data " +
        $"analysis off, and {howItRead}. brisk reads the policy and nothing past " +
        "it: whether this build of Windows has Recall at all is not something " +
        "that read can tell you. brisk does not offer to change this one — the " +
        "setting is new, it differs between builds, and brisk will not make a " +
        "change it cannot check afterwards.";

    /// The one headline in this family whose value is a WORD. Headline's own
    /// doc calls it the number a finding leads with, and the two rules beside
    /// this one lead with a count; this reading is not a count and there is no
    /// number to make out of it, so the word stands where the number would.
    /// Anything that assumes a headline value parses as a number gets a word
    /// from this rule.
    ///
    /// The word is a whole key rather than an argument substituted into one:
    /// an English word passed as an argument would survive translation
    /// untranslated and reach a Turkish reader in English.
    private Headline State(string suffix, string english) => new(
        english, "Recall data analysis — what the policy says",
        $"rule.{Id}.headline.value.{suffix}", Array.Empty<string>(),
        $"rule.{Id}.headline.caption", Array.Empty<string>());

    private static int? Policy(DiagnosticContext ctx)
    {
        try { return ctx.Registry.GetInt(KeyPath, ValueName); }
        catch (Exception) { return null; }
    }
}
