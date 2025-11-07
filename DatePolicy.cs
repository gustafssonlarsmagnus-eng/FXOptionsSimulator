using QLNet;

public sealed record DatePolicy
{
    // Expiry
    public BusinessDayConvention ExpiryConvention { get; init; } = BusinessDayConvention.ModifiedFollowing;
    public bool ExpiryEOM { get; init; } = true;

    // Premium
    public CalendarMode PremiumCalendarMode { get; init; } = CalendarMode.JointPairCurrencies;
    public BusinessDayConvention PremiumConvention { get; init; } = BusinessDayConvention.Following;
    public int PremiumSettleDays { get; init; } = 2;

    // Spot lag resolver (same for everyone)
    public Func<string, PairSpotLag> SpotLagForPair { get; init; } = pair =>
    {
        switch ((pair ?? "").ToUpperInvariant())
        {
            case "USDCAD": return PairSpotLag.OneBD;
            case "USDTRY": return PairSpotLag.OneBD;
            default: return PairSpotLag.TwoBD; // most majors
        }
    };
}

public static class GlobalDatePolicy
{
    public static readonly DatePolicy Policy = new DatePolicy
    {
        ExpiryConvention = BusinessDayConvention.ModifiedFollowing,
        ExpiryEOM = true,
        PremiumCalendarMode = CalendarMode.JointPairCurrencies,
        PremiumConvention = BusinessDayConvention.Following,
        PremiumSettleDays = 2
    };
}
