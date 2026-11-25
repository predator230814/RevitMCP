namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class CSharpQueryBudget
{
    private int _charges;

    public CSharpQueryBudget(int limit)
    {
        Limit = limit;
    }

    public int Limit { get; }

    public void Charge()
    {
        _charges++;
        if (_charges > Limit)
        {
            throw new CSharpQueryBudgetExceededException();
        }
    }
}

internal sealed class CSharpQueryBudgetExceededException : Exception;
