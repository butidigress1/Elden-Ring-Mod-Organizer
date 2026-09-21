namespace EldenRingOrganizer.SmithboxIntegration;

public enum ComparisonState
{
    Unchanged,
    Modified,
    Added
}

public enum ComparisonFilter
{
    All,
    Changed,
    Added,
    Unchanged
}

public static class ComparisonFilterExtensions
{
    public static bool Matches(this ComparisonFilter filter, ComparisonState state)
    {
        return filter switch
        {
            ComparisonFilter.All => true,
            ComparisonFilter.Changed => state is ComparisonState.Modified or ComparisonState.Added,
            ComparisonFilter.Added => state is ComparisonState.Added,
            ComparisonFilter.Unchanged => state is ComparisonState.Unchanged,
            _ => true
        };
    }
}
