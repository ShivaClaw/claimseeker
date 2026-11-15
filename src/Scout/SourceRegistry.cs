using ClaimSeeker.Core;

namespace ClaimSeeker.Scout;

/// <summary>
/// The places ClaimSeeker knows to look. This is a map, not a promise of coverage: only a source with
/// a playbook is searched by the engine. Everything else is shown to the person as somewhere to look
/// themselves, with the official address when that address has been checked.
///
/// An entry address is recorded only when it was seen on the operator's own site or in the project's
/// vetted source list. Where it has not been checked, <see cref="SourceDescriptor.EntryUrl"/> is null
/// and the person is given the operator's name and domain instead of a link that might be wrong.
/// </summary>
public static class SourceRegistry
{
    public const string MissingMoney = "missingmoney";
    public const string HolderDirect = "holder-direct";

    public static IReadOnlyList<SourceDescriptor> All { get; } =
    [
        new SourceDescriptor
        {
            Id = MissingMoney,
            Name = "MissingMoney.com",
            Operator = "National Association of Unclaimed Property Administrators (NAUPA)",
            Mode = InteractionMode.Auto,
            Status = SourceStatus.ProvenLegacy,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["missingmoney.com"],
            EntryUrl = "https://missingmoney.com/app/claim-search",
            CadenceDays = 30,
            PlaybookId = "missingmoney",
            What = "The multi-state search NAUPA endorses. One search covers the unclaimed property programs of the participating states.",
        },
        new SourceDescriptor
        {
            Id = "state-programs",
            Name = "State unclaimed property programs",
            Operator = "Each state's treasurer, controller or revenue department",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["unclaimed.org"],
            EntryUrl = "https://unclaimed.org/search/",
            CadenceDays = 90,
            What = "Every state runs its own program, and its site is where a claim is filed. NAUPA's directory links to each official one.",
        },
        new SourceDescriptor
        {
            Id = "county-surplus",
            Name = "County foreclosure and tax-sale surplus",
            Operator = "County clerks, treasurers and courts",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["cuyahogacounty.gov", "allensuperiorcourt.us", "pinal.gov", "islandcountywa.gov"],
            CadenceDays = 30,
            DeadlineNote = "Deadlines run from months to a few years and differ by county and state. After that the money usually moves to the state's unclaimed property program.",
            What = "When a foreclosed or tax-sold home sells for more than was owed, the difference belongs to the former owner. Each county publishes and releases it in its own way.",
        },
        new SourceDescriptor
        {
            Id = "dol-wow",
            Name = "Workers Owed Wages",
            Operator = "U.S. Department of Labor, Wage and Hour Division",
            Mode = InteractionMode.Auto,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Employer,
            OfficialHosts = ["dol.gov"],
            EntryUrl = "https://webapps.dol.gov/wow/",
            CadenceDays = 90,
            DeadlineNote = "The division holds back wages for three years, then sends them to the U.S. Treasury.",
            What = "Back wages the division collected from an employer but could not deliver to the worker.",
        },
        new SourceDescriptor
        {
            Id = "uscourts-ucfl",
            Name = "U.S. Courts Unclaimed Funds Locator",
            Operator = "U.S. Bankruptcy Courts",
            Mode = InteractionMode.Assisted,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["uscourts.gov"],
            EntryUrl = "https://ucfl.uscourts.gov/",
            CadenceDays = 90,
            What = "Money from bankruptcy cases that a trustee could not deliver. The search asks for a CAPTCHA, so a person runs it; the claim is an application to the court.",
        },
        new SourceDescriptor
        {
            Id = "pbgc-unclaimed",
            Name = "PBGC unclaimed retirement benefits",
            Operator = "Pension Benefit Guaranty Corporation",
            Mode = InteractionMode.Auto,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["pbgc.gov"],
            EntryUrl = "https://www.pbgc.gov/workers-retirees/find-unclaimed-retirement-benefits/search-unclaimed",
            CadenceDays = 180,
            What = "Pension benefits from plans that ended, held for people the plan could not find.",
        },
        new SourceDescriptor
        {
            Id = "fdic-unclaimed",
            Name = "FDIC unclaimed funds",
            Operator = "Federal Deposit Insurance Corporation",
            Mode = InteractionMode.Auto,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["fdic.gov"],
            EntryUrl = "https://closedbanks.fdic.gov/funds/",
            CadenceDays = 180,
            What = "Deposits and dividends owed to customers of banks that failed.",
        },
        new SourceDescriptor
        {
            Id = "ncua-unclaimed",
            Name = "NCUA unclaimed deposits",
            Operator = "National Credit Union Administration",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["ncua.gov"],
            CadenceDays = 180,
            What = "Deposits owed to members of credit unions that were liquidated.",
        },
        new SourceDescriptor
        {
            Id = "dol-lost-and-found",
            Name = "Retirement Savings Lost and Found",
            Operator = "U.S. Department of Labor, Employee Benefits Security Administration",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = ["dol.gov"],
            EntryUrl = "https://lostandfound.dol.gov/",
            CadenceDays = 180,
            What = "Retirement plans from former employers that may still owe a benefit. The search signs in through Login.gov, so the person runs it.",
        },
        new SourceDescriptor
        {
            Id = "treasury-hunt",
            Name = "Treasury Hunt",
            Operator = "U.S. Department of the Treasury, Bureau of the Fiscal Service",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = ["treasurydirect.gov"],
            EntryUrl = "https://www.treasurydirect.gov/savings-bonds/treasury-hunt/",
            CadenceDays = 365,
            What = "Matured savings bonds that have stopped earning interest. The search asks for a Social Security number, so the person runs it; the engine never handles that number.",
        },
        new SourceDescriptor
        {
            Id = "naic-policy-locator",
            Name = "Life Insurance Policy Locator",
            Operator = "National Association of Insurance Commissioners",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = ["naic.org"],
            EntryUrl = "https://eapps.naic.org/life-policy-locator/",
            CadenceDays = 365,
            What = "Asks participating insurers whether someone who has died held a life policy or annuity. Insurers answer the person directly.",
        },
        new SourceDescriptor
        {
            Id = "hud-fha-refunds",
            Name = "FHA mortgage insurance refunds",
            Operator = "U.S. Department of Housing and Urban Development",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["hud.gov"],
            CadenceDays = 365,
            What = "Refunds owed to some people who had an FHA-insured mortgage.",
        },
        new SourceDescriptor
        {
            Id = "va-insurance-funds",
            Name = "VA unclaimed insurance funds",
            Operator = "U.S. Department of Veterans Affairs",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["va.gov"],
            CadenceDays = 365,
            What = "Insurance money owed to veterans or their beneficiaries that the VA could not deliver.",
        },
        new SourceDescriptor
        {
            Id = "irs-refund",
            Name = "IRS refund status",
            Operator = "Internal Revenue Service",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = ["irs.gov"],
            CadenceDays = 365,
            DeadlineNote = "A refund generally has to be claimed within three years of the return's due date.",
            What = "Refunds the IRS could not deliver, and refunds never claimed because a return was not filed. The check asks for a Social Security number, so the person runs it.",
        },
        new SourceDescriptor
        {
            Id = "doi-trust-accounts",
            Name = "Individual Indian Money accounts",
            Operator = "U.S. Department of the Interior",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.Name,
            OfficialHosts = ["doi.gov"],
            EntryUrl = "https://www.doi.gov/ost/search-unclaimed-accounts",
            CadenceDays = 365,
            What = "Trust accounts held for individual American Indians and Alaska Natives whose whereabouts are unknown.",
        },
        new SourceDescriptor
        {
            Id = "class-actions",
            Name = "Class action settlements",
            Operator = "Settlement administrators, as listed by ClassAction.org",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = ["classaction.org"],
            EntryUrl = "https://www.classaction.org/settlements",
            CadenceDays = 14,
            DeadlineNote = "Each settlement has its own claim deadline.",
            What = "Open settlements a person may qualify for. A claim is a statement made under penalty of perjury, so the person decides whether they qualify and files it themselves.",
        },
        new SourceDescriptor
        {
            Id = "usa-gov",
            Name = "USA.gov unclaimed money guide",
            Operator = "U.S. General Services Administration",
            Mode = InteractionMode.Guided,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = ["usa.gov"],
            EntryUrl = "https://www.usa.gov/unclaimed-money",
            CadenceDays = 365,
            What = "The federal government's own index of official places to look.",
        },
        new SourceDescriptor
        {
            Id = HolderDirect,
            Name = "The holder itself",
            Operator = "The company that owes the money",
            Mode = InteractionMode.Draft,
            Status = SourceStatus.Planned,
            QueryBy = QueryBy.None,
            OfficialHosts = [],
            CadenceDays = 365,
            What = "Money a company still holds and has not yet turned over to a state. There is nothing to search; the engine drafts a letter for the person to send.",
        },
    ];

    public static SourceDescriptor? Find(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// How the engine actually treats a source today. A source the map marks auto or assisted is
    /// still a guided one until it has a playbook: without a recipe the engine does not touch it.
    /// </summary>
    public static InteractionMode EffectiveMode(SourceDescriptor source) =>
        source.PlaybookId is null && source.Mode is InteractionMode.Auto or InteractionMode.Assisted
            ? InteractionMode.Guided
            : source.Mode;

    /// <summary>The sources the engine can search itself.</summary>
    public static IReadOnlyList<SourceDescriptor> Searchable =>
        All.Where(s => s.PlaybookId is not null && s.Mode == InteractionMode.Auto).ToList();
}
