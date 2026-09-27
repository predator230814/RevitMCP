namespace RevitMCP.Addin.Intents;

internal abstract record IntentTypedValue
{
    private IntentTypedValue()
    {
    }

    internal sealed record StringValue(string Value) : IntentTypedValue;

    internal sealed record IntegerValue(int Value) : IntentTypedValue;

    internal sealed record QuantityValue(double Value, string UnitTypeId) : IntentTypedValue;
}
