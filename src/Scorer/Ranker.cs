using ClaimSeeker.Core;

namespace ClaimSeeker.Scorer;

public sealed record RankInput(
    MoneyRange Value,
    PropertyFamily Family,
    Confidence Confidence,
    bool NameChangeDocsLikely,
    bool DeceasedOwner,
    InteractionMode Mode,
    bool ManualFallback,
    DateOnly? Deadline);

public sealed record RankResult(
    decimal ExpectedValue,
    decimal EffortHours,
    decimal Score,
    bool Urgent,
    IReadOnlyList<string> Rationale);

/// <summary>
/// Orders findings by what they are likely worth for the time they are likely to take, so the person
/// starts with the claim that pays best per hour. Every number here is a prior for sorting. None of
/// them is shown to the person as an amount of money: reports total only what sources actually said.
///
/// Two rules sit above the arithmetic. A "possible" match never outranks a "likely" or "strong" one,
/// whatever it might be worth, so a large speculative number cannot top the list. And a finding
/// whose deadline is close goes to the front of its group, because money that is about to move is
/// worth more today than it will be next month.
/// </summary>
public static class Ranker
{
    public const decimal BaseEffortHours = 0.5m;
    public const decimal NameChangeHours = 0.5m;
    public const decimal DeceasedOwnerHours = 1.5m;
    public const decimal NotarizationHours = 1.0m;
    public const decimal ManualFallbackHours = 0.5m;

    /// <summary>
    /// States commonly ask for a notarized claim form once a claim is large. The exact threshold is
    /// each state's own; until the per-state requirements are recorded, this is the working figure.
    /// </summary>
    public const decimal NotarizationLikelyAbove = 1000m;

    public const int UrgentWithinDays = 60;

    /// <summary>
    /// How much more than its stated floor an open-ended amount tends to be worth, by kind. "Over
    /// $100" of stock is usually far over; "over $100" of utility deposit is usually not.
    /// </summary>
    public static decimal TypeMultiplier(PropertyFamily family) => family switch
    {
        PropertyFamily.Securities or PropertyFamily.Insurance or PropertyFamily.Retirement => 3.0m,
        PropertyFamily.AccountBalance => 2.0m,
        PropertyFamily.Wages => 1.5m,
        PropertyFamily.Utility => 1.1m,
        _ => 1.0m,
    };

    public static decimal ModeHours(InteractionMode mode) => mode switch
    {
        InteractionMode.Assisted => 0.25m,
        InteractionMode.Guided or InteractionMode.Draft => 0.5m,
        _ => 0m,
    };

    public static RankResult Rank(RankInput input, DateOnly today)
    {
        var rationale = new List<string>();

        decimal expected;
        if (!input.Value.Disclosed)
        {
            expected = 0m;
            rationale.Add("The amount is not disclosed, so this is ordered by effort alone.");
        }
        else if (input.Value.IsOpenEnded)
        {
            var multiplier = TypeMultiplier(input.Family);
            expected = input.Value.Floor * multiplier;
            rationale.Add($"Open-ended amount: the stated floor times {multiplier:0.0} for {Naupa.Describe(input.Family)}.");
        }
        else
        {
            expected = ((input.Value.Low ?? 0m) + (input.Value.High ?? 0m)) / 2m;
            rationale.Add(input.Value.IsExact ? "Exact amount." : "Midpoint of the stated range.");
        }

        var effort = BaseEffortHours;
        if (input.NameChangeDocsLikely) { effort += NameChangeHours; rationale.Add("Filed under a former name: proof of the name change will be needed."); }
        if (input.DeceasedOwner) { effort += DeceasedOwnerHours; rationale.Add("The owner has died: a death certificate and proof of heirship will be needed."); }
        if (expected >= NotarizationLikelyAbove) { effort += NotarizationHours; rationale.Add("Large enough that the claim form will likely need a notary."); }
        if (input.ManualFallback) { effort += ManualFallbackHours; rationale.Add("The source asked for a person, so the search was run by hand."); }
        var modeHours = ModeHours(input.Mode);
        if (modeHours > 0m) { effort += modeHours; rationale.Add($"This source is {input.Mode.ToString().ToLowerInvariant()}: part of the work is the person's."); }

        var urgent = input.Deadline is { } deadline
                     && deadline >= today
                     && deadline.DayNumber - today.DayNumber <= UrgentWithinDays;
        if (urgent) rationale.Add($"The deadline is within {UrgentWithinDays} days.");

        var score = decimal.Round(expected / effort, 2, MidpointRounding.AwayFromZero);
        return new RankResult(decimal.Round(expected, 2, MidpointRounding.AwayFromZero), effort, score, urgent, rationale);
    }

    /// <summary>
    /// Negative when <paramref name="a"/> belongs above <paramref name="b"/>. Trusted matches first,
    /// then the urgent, then the score, then the larger amount.
    /// </summary>
    public static int Compare((Confidence Confidence, bool Urgent, decimal Score, decimal ExpectedValue) a,
        (Confidence Confidence, bool Urgent, decimal Score, decimal ExpectedValue) b)
    {
        var tier = Tier(b.Confidence).CompareTo(Tier(a.Confidence));
        if (tier != 0) return tier;
        var urgent = b.Urgent.CompareTo(a.Urgent);
        if (urgent != 0) return urgent;
        var score = b.Score.CompareTo(a.Score);
        if (score != 0) return score;
        return b.ExpectedValue.CompareTo(a.ExpectedValue);
    }

    /// <summary>1 for a match the engine trusts enough to act on, 0 for one it only suspects.</summary>
    public static int Tier(Confidence confidence) => confidence >= Confidence.Likely ? 1 : 0;
}
