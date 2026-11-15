namespace ClaimSeeker.Core;

/// <summary>
/// The broad kinds of unclaimed property, following the two-letter families of the NAUPA standard
/// property-type codes that holders use when they report to a state (AC01, CK13, SC08 and so on).
/// The engine aligns at the family level: that is what the ranking priors and the document
/// checklists need, and it does not depend on a state's own wording for an individual code.
/// </summary>
public enum PropertyFamily
{
    Unknown,
    AccountBalance,   // AC  checking, savings, matured CDs, security deposits
    UncashedCheck,    // CK  cashier's checks, money orders, vendor checks
    CourtDeposit,     // CT  escrow, condemnation awards, missing heirs' funds
    Insurance,        // IN  policy benefits, matured policies, premium refunds
    MineralProceeds,  // MI  royalties and working interests
    Wages,            // MS01-MS02  wages, payroll, commissions
    Miscellaneous,    // MS  refunds, credit balances, overpayments
    Securities,       // SC  dividends, shares, mutual funds, bond principal
    SafeDeposit,      // SD  safe deposit box contents and other tangible property
    Trust,            // TR  fiduciary funds, paying-agent accounts, escrow
    Utility,          // UT  utility deposits, refunds, capital credits
    Retirement,       // IR  IRAs; also pension and profit-sharing balances
    HealthSavings,    // HS
    EducationSavings, // CS
}

public static class Naupa
{
    /// <summary>The family a NAUPA code such as "AC01" belongs to, by its two-letter prefix.</summary>
    public static PropertyFamily FamilyFromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return PropertyFamily.Unknown;
        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length < 4 || !char.IsAsciiDigit(trimmed[2]) || !char.IsAsciiDigit(trimmed[3])) return PropertyFamily.Unknown;

        return trimmed[..2] switch
        {
            "AC" => PropertyFamily.AccountBalance,
            "CK" => PropertyFamily.UncashedCheck,
            "CT" => PropertyFamily.CourtDeposit,
            "IN" => PropertyFamily.Insurance,
            "MI" => PropertyFamily.MineralProceeds,
            "MS" => trimmed[2..4] is "01" or "02" ? PropertyFamily.Wages : PropertyFamily.Miscellaneous,
            "SC" => PropertyFamily.Securities,
            "SD" => PropertyFamily.SafeDeposit,
            "TR" => PropertyFamily.Trust,
            "UT" => PropertyFamily.Utility,
            "IR" => PropertyFamily.Retirement,
            "HS" => PropertyFamily.HealthSavings,
            "CS" => PropertyFamily.EducationSavings,
            _ => PropertyFamily.Unknown,
        };
    }

    /// <summary>
    /// The family for a source's property-type text. A leading NAUPA code wins; otherwise the words
    /// decide, in an order that puts the specific before the general ("utility deposit" is a utility
    /// refund, not a bank deposit; a "security deposit" is not a security).
    /// </summary>
    public static PropertyFamily Classify(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return PropertyFamily.Unknown;
        var tokens = NameText.Tokens(description);
        if (tokens.Count == 0) return PropertyFamily.Unknown;

        var fromCode = FamilyFromCode(tokens[0]);
        if (fromCode != PropertyFamily.Unknown) return fromCode;

        var text = " " + string.Join(' ', tokens) + " ";
        foreach (var (family, phrases) in Rules)
            foreach (var phrase in phrases)
                if (text.Contains(phrase, StringComparison.Ordinal)) return family;

        return PropertyFamily.Unknown;
    }

    /// <summary>Plain words for a family, for reports and packets.</summary>
    public static string Describe(PropertyFamily family) => family switch
    {
        PropertyFamily.AccountBalance => "bank account or deposit",
        PropertyFamily.UncashedCheck => "uncashed check",
        PropertyFamily.CourtDeposit => "court deposit",
        PropertyFamily.Insurance => "insurance proceeds",
        PropertyFamily.MineralProceeds => "mineral proceeds",
        PropertyFamily.Wages => "wages",
        PropertyFamily.Miscellaneous => "refund or credit",
        PropertyFamily.Securities => "securities",
        PropertyFamily.SafeDeposit => "safe deposit contents",
        PropertyFamily.Trust => "trust or escrow funds",
        PropertyFamily.Utility => "utility refund",
        PropertyFamily.Retirement => "retirement account",
        PropertyFamily.HealthSavings => "health savings account",
        PropertyFamily.EducationSavings => "education savings account",
        _ => "unclaimed property",
    };

    // Phrases carry their own spaces so they match whole words: " IRA " never fires inside "MIRACLE".
    private static readonly (PropertyFamily Family, string[] Phrases)[] Rules =
    [
        (PropertyFamily.Utility, [" UTILITY ", " UTILITIES ", " CAPITAL CREDIT", " MEMBERSHIP FEE"]),
        (PropertyFamily.AccountBalance, [" SECURITY DEPOSIT"]),
        (PropertyFamily.Wages, [" WAGE", " PAYROLL ", " SALARY ", " SALARIES ", " COMMISSION", " PAYCHECK"]),
        (PropertyFamily.UncashedCheck, [" PENSION CHECK"]),
        (PropertyFamily.Retirement, [" IRA ", " 401K ", " 401 K ", " PENSION", " RETIREMENT ", " KEOGH ", " PROFIT SHARING "]),
        (PropertyFamily.HealthSavings, [" HEALTH SAVINGS ", " HSA "]),
        (PropertyFamily.EducationSavings, [" EDUCATION SAVINGS ", " EDUCATIONAL SAVINGS ", " COVERDELL ", " 529 "]),
        (PropertyFamily.CourtDeposit, [" COURT ", " CONDEMNATION ", " MISSING HEIR", " RESTITUTION ", " BAIL "]),
        (PropertyFamily.Insurance, [" INSURANCE ", " POLICY ", " POLICIES ", " ANNUITY ", " ANNUITIES ", " PREMIUM ", " ENDOWMENT", " BENEFICIAR", " DEATH BENEFIT", " CLAIM PAYMENT"]),
        (PropertyFamily.Securities, [" SECURITIES ", " SECURITY ", " STOCK", " SHARE", " DIVIDEND", " MUTUAL FUND", " BOND ", " BONDS ", " DEBENTURE"]),
        (PropertyFamily.SafeDeposit, [" SAFE DEPOSIT ", " SAFEKEEPING ", " TANGIBLE "]),
        (PropertyFamily.MineralProceeds, [" ROYALTY ", " ROYALTIES ", " MINERAL", " WORKING INTEREST ", " OIL AND GAS ", " OIL GAS "]),
        (PropertyFamily.Trust, [" TRUST ", " FIDUCIARY ", " ESCROW ", " PAYING AGENT ", " FUNERAL "]),
        (PropertyFamily.UncashedCheck, [" CASHIER", " MONEY ORDER", " TRAVELER", " CHECK ", " CHECKS ", " DRAFT ", " DRAFTS ", " WARRANT ", " WARRANTS "]),
        (PropertyFamily.AccountBalance, [" CHECKING ", " SAVINGS ", " CERTIFICATE OF DEPOSIT ", " CD ", " ACCOUNT BALANCE", " DEPOSIT ", " DEPOSITS ", " CHRISTMAS CLUB "]),
        (PropertyFamily.Miscellaneous, [" REFUND", " REBATE", " CREDIT BALANCE", " OVERPAYMENT", " GIFT CERTIFICATE", " GIFT CARD", " ACCOUNTS PAYABLE "]),
    ];
}
