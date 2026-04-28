using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace MetaRecord.Workflows.Runtime.Executors;

internal static class WorkflowConditionEvaluator
{
    public static bool Evaluate(JsonElement condition, WorkflowExecutionContext context)
    {
        if (condition.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Condition config must be an object.");

        var left = WorkflowValueResolver.ResolveOperand(GetRequiredProperty(condition, "left"), context);
        var operatorName = GetRequiredString(condition, "operator");
        var requiresRight = !string.Equals(operatorName, "isEmpty", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(operatorName, "isNotEmpty", StringComparison.OrdinalIgnoreCase);
        var right = requiresRight ? WorkflowValueResolver.ResolveOperand(GetRequiredProperty(condition, "right"), context) : null;

        return operatorName switch
        {
            "equals" => AreEqual(left, right),
            "notEquals" => !AreEqual(left, right),
            "greaterThan" => Compare(left, right) > 0,
            "greaterThanOrEqual" => Compare(left, right) >= 0,
            "lessThan" => Compare(left, right) < 0,
            "lessThanOrEqual" => Compare(left, right) <= 0,
            "contains" => ToText(left).Contains(ToText(right), StringComparison.OrdinalIgnoreCase),
            "startsWith" => ToText(left).StartsWith(ToText(right), StringComparison.OrdinalIgnoreCase),
            "endsWith" => ToText(left).EndsWith(ToText(right), StringComparison.OrdinalIgnoreCase),
            "isEmpty" => IsEmpty(left),
            "isNotEmpty" => !IsEmpty(left),
            _ => throw new InvalidOperationException($"Condition operator '{operatorName}' is not supported.")
        };
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        if (TryGetDecimal(left, out var leftDecimal) && TryGetDecimal(right, out var rightDecimal))
            return leftDecimal == rightDecimal;

        if (TryGetDateTime(left, out var leftDateTime) && TryGetDateTime(right, out var rightDateTime))
            return leftDateTime == rightDateTime;

        if (TryGetBool(left, out var leftBool) && TryGetBool(right, out var rightBool))
            return leftBool == rightBool;

        return string.Equals(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
    }

    private static int Compare(object? left, object? right)
    {
        if (TryGetDecimal(left, out var leftDecimal) && TryGetDecimal(right, out var rightDecimal))
            return leftDecimal.CompareTo(rightDecimal);

        if (TryGetDateTime(left, out var leftDateTime) && TryGetDateTime(right, out var rightDateTime))
            return leftDateTime.CompareTo(rightDateTime);

        return string.Compare(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmpty(object? value)
    {
        if (value is null)
            return true;
        if (value is string stringValue)
            return string.IsNullOrWhiteSpace(stringValue);
        if (value is IEnumerable enumerable)
            return !enumerable.GetEnumerator().MoveNext();
        return false;
    }

    private static string ToText(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool TryGetDecimal(object? value, out decimal result)
    {
        result = default;
        return value switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                decimal.TryParse(ToText(value), NumberStyles.Any, CultureInfo.InvariantCulture, out result),
            string stringValue => decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out result),
            _ => false
        };
    }

    private static bool TryGetDateTime(object? value, out DateTime result)
    {
        result = default;
        return value switch
        {
            DateTime dateTime => (result = dateTime) == dateTime,
            string stringValue => DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out result),
            _ => false
        };
    }

    private static bool TryGetBool(object? value, out bool result)
    {
        result = default;
        return value switch
        {
            bool boolValue => (result = boolValue) == boolValue,
            string stringValue => bool.TryParse(stringValue, out result),
            _ => false
        };
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
            return value;

        throw new InvalidOperationException($"Condition must include '{propertyName}'.");
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString()!;

        throw new InvalidOperationException($"Condition must include '{propertyName}'.");
    }
}