using RevitMCP.Addin.Intents;

namespace RevitMCP.Addin.Inspection;

internal static class ApplyBeforeStateComparer
{
    public static bool Matches(IntentItemEntry item, bool hasValue, IntentTypedValue? current)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.BeforeHasValue != hasValue)
        {
            return false;
        }

        if (!hasValue)
        {
            return current is null;
        }

        return (item.BeforeValue, current) switch
        {
            (IntentTypedValue.StringValue stored, IntentTypedValue.StringValue live) =>
                string.Equals(stored.Value, live.Value, StringComparison.Ordinal),
            (IntentTypedValue.IntegerValue stored, IntentTypedValue.IntegerValue live) =>
                stored.Value == live.Value,
            (IntentTypedValue.QuantityValue stored, IntentTypedValue.QuantityValue live) =>
                stored.Value == live.Value
                && string.Equals(stored.UnitTypeId, live.UnitTypeId, StringComparison.Ordinal),
            _ => false
        };
    }
}
