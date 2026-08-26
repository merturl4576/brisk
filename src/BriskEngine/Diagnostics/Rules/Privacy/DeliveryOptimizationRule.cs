using System;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// How many bytes this machine uploaded to other machines this month, as
/// Windows' own Delivery Optimization counter has it. The only disclosure in
/// this wave that reads something other than the registry, and the only
/// report-only disclosure that answers one of its readings with silence.
/// Not the only privacy rule that does: TelemetrySwitchRule.Detect returns
/// null for a switch that reads as off, so all six switches stay quiet on a
/// reading they took. What is new here is a DISCLOSURE doing it — the three
/// beside this one always return a finding.
///
/// A NUMBER, NOTHING, AND NO ANSWER ARE THREE THINGS.
///
/// A count of nothing is not a disclosure. The three registry disclosures
/// beside this one report every count they can make; a machine that uploaded
/// no bytes has nothing to disclose, and a row carrying that zero would lead
/// with a number no reader needs. So zero is silence here — the silence the
/// six switches beside it already use, arriving in a disclosure for the
/// first time.
///
/// A counter brisk could not read is NOT that silence. It is the wave's
/// standing rule applied to a probe instead of a key: an unreadable read
/// reports unreadable, never zero, because "nothing left this machine" and
/// "brisk could not find out what left this machine" are different claims
/// and only the first one is reassuring. The finding that says so carries no
/// Headline — a headline is what a finding leads with, and brisk has no
/// reading to lead with — and no digit anywhere a reader can see one.
public sealed class DeliveryOptimizationRule : PrivacyDisclosureRule
{
    /// THE SAME STRING IS ALSO A CLEANUP TARGET ID. CleanupTargetRegistry
    /// has a `delivery-optimization` target: the Delivery Optimization cache
    /// folder. Nothing collides — rule ids and target ids live in separate
    /// registries, `--rule` and `--target` read different sets, and the resx
    /// keys are `rule.*` against `clean.target.*` — but `brisk rules` and
    /// `brisk targets` both print this string now, meaning different things.
    /// They are also not two views of one thing. REASONED, NOT OBSERVED:
    /// this rule's number is a monthly total of bytes already uploaded, so
    /// emptying that cache frees disk and should not move it — nobody has
    /// emptied it and re-read the counter here to confirm that.
    public override string Id => "delivery-optimization";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx) =>
        Uploaded(ctx) switch
        {
            // Anything the probe hands back that is not a count of bytes
            // brisk can report. null is the probe's own way of saying it
            // could not read the counter, and a figure below zero is not a
            // quantity of anything; neither is rounded into the answer that
            // would reassure, and neither is the whole of what lands here.
            //
            // THREE FIGURES ARE CHECKED WHERE THERE USED TO BE ONE, because
            // the probe now hands back two halves and their sum rather than a
            // single number. Each half in its own right — a half below zero
            // is not a quantity even when the other half hides it in the
            // total — and the total in its own right too, which is not
            // implied by the halves: two halves large enough to wrap the sum
            // negative are each individually non-negative.
            null or { LanBytes: < 0 } or { InternetBytes: < 0 } or { Total: < 0 }
                => Unread(),
            { Total: 0 } => null,
            { } upload => Reported(upload),
        };

    /// THE HEADLINE IS THE TOTAL AND THE SENTENCE IS THE SPLIT. Windows
    /// counts these bytes in two halves — machines reached over this local
    /// network, machines reached over the internet — and brisk used to add
    /// them and report the sum alone, which told a reader that 302 MB left
    /// this machine and not that every byte of it stopped at the router. The
    /// two are different claims about one number and the split is the one
    /// brisk read. The lead stays the total because a headline is one figure
    /// and the number that answers "how much" is the sum of both halves.
    ///
    /// THE LAST CLAUSE IS NOT A LEFTOVER. Naming the two sides is not naming
    /// the machines, and a reader handed a split is likelier to think brisk
    /// knows where the bytes landed than one handed a total. So the sentence
    /// that says brisk cannot say which machines those were survives the
    /// widening, and the test on it says why it is there.
    private DiagnosticFinding Reported(PeerUpload upload)
    {
        var amount = Fmt.Bytes(upload.Total);
        var lan = Fmt.Bytes(upload.LanBytes);
        var internet = Fmt.Bytes(upload.InternetBytes);
        return Disclosure(
            $"rule.{Id}.title",
            "Windows uploaded data from this machine to other machines this month",
            $"rule.{Id}.evidence",
            "Delivery Optimization is the part of Windows that uploads content " +
            "from this machine to other machines. Windows keeps a running count " +
            "of what it uploaded that way, and for the current calendar month " +
            $"that counter reads {amount}: {lan} of it to machines on this local " +
            $"network, {internet} to machines on the internet. brisk reads the " +
            "counter and nothing past it: which machines those were is not " +
            "something that read can tell you.",
            new[] { amount, lan, internet },
            new Headline(
                amount, "uploaded from this machine to other machines this month",
                $"rule.{Id}.headline.value", new[] { amount },
                $"rule.{Id}.headline.caption", Array.Empty<string>()));
    }

    /// No headline, and no number in any sentence: the finding's whole
    /// content is that brisk asked and did not get an answer it could use.
    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread",
        "How much this machine uploaded to other machines could not be established",
        $"rule.{Id}.evidence.unread",
        "brisk asked Windows for its Delivery Optimization counter and did not " +
        "get a number back that it can read. A machine that uploaded nothing " +
        "and a machine brisk could not ask are different things, so brisk does " +
        "not report a count of none.");

    /// The catch is here as well as inside the probe. This rule runs inside
    /// a scan, and an exception that got past both would reach EngineHost's
    /// catch-all, which drops the whole finding without a word — and the
    /// reading brisk would then never make is the unreadable one, which is
    /// the reading this rule exists to make.
    private static PeerUpload? Uploaded(DiagnosticContext ctx)
    {
        try { return ctx.DeliveryOptimization.UploadedToPeers(); }
        catch (Exception) { return null; }
    }
}
