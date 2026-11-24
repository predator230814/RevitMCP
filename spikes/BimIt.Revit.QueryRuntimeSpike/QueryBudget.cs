namespace BimIt.Revit.QueryRuntimeSpike;

/// <summary>
/// Host-owned cooperative budget. Loop instrumentation calls <see cref="Charge"/>.
/// </summary>
public sealed class QueryBudget
{
    public const string BlockingCallLimitation =
        "A blocking Revit API call cannot be safely interrupted. The budget check runs when an instrumented loop body is entered.";

    private int _charges;

    public QueryBudget(int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        Limit = limit;
    }

    public int Limit { get; }

    public int Charges => _charges;

    public void Charge()
    {
        _charges++;
        if (_charges > Limit)
        {
            throw new QueryBudgetExceededException(Limit);
        }
    }
}

public sealed class QueryBudgetExceededException : Exception
{
    public QueryBudgetExceededException(int limit)
        : base("execution budget exceeded (limit " + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")")
    {
        Limit = limit;
    }

    public int Limit { get; }
}
