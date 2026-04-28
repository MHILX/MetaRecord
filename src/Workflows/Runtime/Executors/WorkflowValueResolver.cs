using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

internal static partial class WorkflowValueResolver
{
    private static readonly Regex PlaceholderPattern = CreatePlaceholderPattern();

    public static string GetRequiredString(WorkflowNode node, string propertyName)
    {
        var value = GetRequiredProperty(node, propertyName);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Config field '{propertyName}' must be a non-empty string.");

        return value.GetString()!;
    }

    public static string? GetOptionalString(WorkflowNode node, string propertyName)
    {
        if (!TryGetProperty(node.Config, propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Config field '{propertyName}' must be a string.");

        return value.GetString();
    }

    public static JsonElement GetRequiredProperty(WorkflowNode node, string propertyName)
    {
        if (!TryGetProperty(node.Config, propertyName, out var value) || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException($"Config field '{propertyName}' is required.");

        return value;
    }

    public static object? ResolveConfiguredValue(JsonElement value, WorkflowExecutionContext context) => value.ValueKind switch
    {
        JsonValueKind.Undefined => null,
        JsonValueKind.Null => null,
        JsonValueKind.String => ResolveTemplateValue(value.GetString() ?? string.Empty, context),
        JsonValueKind.Number => ResolveNumber(value),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Object when TryGetString(value, "source", out _) => ResolveOperand(value, context),
        _ => value.GetRawText()
    };

    public static string ResolveTemplate(string template, WorkflowExecutionContext context)
    {
        var resolved = ResolveTemplateValue(template, context);
        return Convert.ToString(resolved, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public static object? ResolveTemplateValue(string template, WorkflowExecutionContext context)
    {
        var matches = PlaceholderPattern.Matches(template);
        if (matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == template.Length)
            return ResolvePath(matches[0].Groups["path"].Value, context);

        var resolved = PlaceholderPattern.Replace(template, match =>
        {
            var value = ResolvePath(match.Groups["path"].Value, context);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        });

        if (resolved.Contains("{{", StringComparison.Ordinal) || resolved.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException($"Template '{template}' contains an invalid placeholder.");

        return resolved;
    }

    public static object? ResolveOperand(JsonElement operand, WorkflowExecutionContext context)
    {
        if (operand.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Condition operand must be an object.");

        var source = GetRequiredString(operand, "source", "Condition operand source is required.");
        return source switch
        {
            "currentRecord" => ResolveDictionaryField(context.CurrentRecord, GetRequiredString(operand, "field", "currentRecord operand field is required."), "currentRecord"),
            "originalRecord" => ResolveDictionaryField(context.OriginalRecord, GetRequiredString(operand, "field", "originalRecord operand field is required."), "originalRecord"),
            "event" => ResolveEventField(context, GetRequiredString(operand, "field", "event operand field is required.")),
            "literal" => TryGetProperty(operand, "value", out var literalValue) ? ResolveConfiguredValue(literalValue, context) : null,
            "variable" => ResolveDictionaryField(context.Variables, GetRequiredString(operand, "name", "Variable operand name is required."), "variable"),
            _ => throw new InvalidOperationException($"Operand source '{source}' is not supported.")
        };
    }

    public static object? ConvertToPropertyValue(object? value, PropertyMetadata property)
    {
        if (value is null)
            return null;

        var targetType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (targetType == typeof(string))
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(Guid))
            return value is Guid guidValue ? guidValue : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        if (targetType == typeof(DateTime))
            return value is DateTime dateTimeValue ? dateTimeValue : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        if (targetType == typeof(bool))
            return value is bool boolValue ? boolValue : bool.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        if (targetType.IsEnum)
            return Enum.Parse(targetType, Convert.ToString(value, CultureInfo.InvariantCulture)!, ignoreCase: true);

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    public static PropertyMetadata GetRequiredPropertyMetadata(IObjectMetadata metadata, string propertyName)
    {
        var property = metadata.Properties.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        return property ?? throw new InvalidOperationException($"Field '{propertyName}' does not exist on object '{metadata.Name}'.");
    }

    public static PropertyMetadata? GetPrimaryKeyProperty(IObjectMetadata metadata) =>
        metadata.Properties.FirstOrDefault(property => property.IsPrimaryKey) ??
        metadata.Properties.FirstOrDefault(property => string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase));

    private static object? ResolvePath(string path, WorkflowExecutionContext context)
    {
        var parts = path.Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new InvalidOperationException($"Template placeholder '{path}' is not supported.");

        return parts[0] switch
        {
            "currentRecord" => ResolveDictionaryField(context.CurrentRecord, parts[1], "currentRecord"),
            "originalRecord" => ResolveDictionaryField(context.OriginalRecord, parts[1], "originalRecord"),
            "event" => ResolveEventField(context, parts[1]),
            "variable" or "variables" => ResolveDictionaryField(context.Variables, parts[1], "variable"),
            _ => throw new InvalidOperationException($"Template source '{parts[0]}' is not supported.")
        };
    }

    private static object? ResolveDictionaryField(IReadOnlyDictionary<string, object?> values, string fieldName, string sourceName)
    {
        if (values.TryGetValue(fieldName, out var value))
            return value;

        throw new InvalidOperationException($"Value '{sourceName}.{fieldName}' was not found.");
    }

    private static object? ResolveEventField(WorkflowExecutionContext context, string fieldName) => fieldName switch
    {
        "WorkflowId" => context.WorkflowId,
        "RunId" => context.RunId,
        "ObjectName" => context.ObjectName,
        "EventName" => context.EventName,
        "RecordId" => context.RecordId,
        _ => throw new InvalidOperationException($"Event field '{fieldName}' is not supported.")
    };

    private static object ResolveNumber(JsonElement value)
    {
        if (value.TryGetInt32(out var intValue))
            return intValue;
        if (value.TryGetInt64(out var longValue))
            return longValue;
        if (value.TryGetDecimal(out var decimalValue))
            return decimalValue;
        return value.GetDouble();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return element.TryGetProperty(propertyName, out value);

        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.String)
            return false;

        value = propertyValue.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetRequiredString(JsonElement element, string propertyName, string message)
    {
        if (!TryGetString(element, propertyName, out var value))
            throw new InvalidOperationException(message);

        return value;
    }

    [GeneratedRegex(@"\{\{\s*(?<path>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)?)\s*\}\}")]
    private static partial Regex CreatePlaceholderPattern();
}