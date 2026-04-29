using System.Text.Json;
using MetaRecord.Workflows.Runtime;
using MetaRecord.Workflows.Runtime.Executors;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class ConditionEvaluatorTests
{
    [Theory]
    [InlineData("equals", "Quantity", "5", true)]
    [InlineData("notEquals", "Quantity", "3", true)]
    [InlineData("greaterThan", "Quantity", "3", true)]
    [InlineData("greaterThanOrEqual", "Quantity", "5", true)]
    [InlineData("lessThan", "Quantity", "8", true)]
    [InlineData("lessThanOrEqual", "Quantity", "5", true)]
    [InlineData("greaterThan", "Quantity", "8", false)]
    public void Evaluate_compares_numeric_values(string operatorName, string fieldName, string literalValue, bool expected)
    {
        var result = WorkflowConditionEvaluator.Evaluate(
            Condition($$"""
            {
              "left": { "source": "currentRecord", "field": "{{fieldName}}" },
              "operator": "{{operatorName}}",
              "right": { "source": "literal", "value": {{literalValue}} }
            }
            """),
            CreateContext());

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("contains", "idg", true)]
    [InlineData("startsWith", "wid", true)]
    [InlineData("endsWith", "GET", true)]
    [InlineData("contains", "missing", false)]
    public void Evaluate_compares_text_without_case_sensitivity(string operatorName, string literalValue, bool expected)
    {
        var result = WorkflowConditionEvaluator.Evaluate(
            Condition($$"""
            {
              "left": { "source": "currentRecord", "field": "Name" },
              "operator": "{{operatorName}}",
              "right": { "source": "literal", "value": "{{literalValue}}" }
            }
            """),
            CreateContext());

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("isEmpty", "Notes", true)]
    [InlineData("isNotEmpty", "Name", true)]
    public void Evaluate_handles_empty_operators_without_right_operand(string operatorName, string fieldName, bool expected)
    {
        var result = WorkflowConditionEvaluator.Evaluate(
            Condition($$"""
            {
              "left": { "source": "currentRecord", "field": "{{fieldName}}" },
              "operator": "{{operatorName}}"
            }
            """),
            CreateContext());

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_compares_dates_and_booleans()
    {
        var context = CreateContext();

        var dateResult = WorkflowConditionEvaluator.Evaluate(
            Condition("""
            {
              "left": { "source": "currentRecord", "field": "ExpiresAt" },
              "operator": "greaterThan",
              "right": { "source": "literal", "value": "2026-04-27T00:00:00Z" }
            }
            """),
            context);

        var boolResult = WorkflowConditionEvaluator.Evaluate(
            Condition("""
            {
              "left": { "source": "currentRecord", "field": "IsActive" },
              "operator": "equals",
              "right": { "source": "literal", "value": "true" }
            }
            """),
            context);

        Assert.True(dateResult);
        Assert.True(boolResult);
    }

    [Fact]
    public void Evaluate_supports_case_insensitive_sources_and_operators()
    {
        var result = WorkflowConditionEvaluator.Evaluate(
            Condition("""
            {
              "left": { "source": "CURRENTRECORD", "field": "Quantity" },
              "operator": "GREATERTHAN",
              "right": { "source": "LITERAL", "value": 3 }
            }
            """),
            CreateContext());

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_throws_clear_error_for_unsupported_operator()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => WorkflowConditionEvaluator.Evaluate(
            Condition("""
            {
              "left": { "source": "currentRecord", "field": "Quantity" },
              "operator": "between",
              "right": { "source": "literal", "value": 3 }
            }
            """),
            CreateContext()));

        Assert.Contains("between", exception.Message);
    }

    private static WorkflowExecutionContext CreateContext() => new()
    {
        WorkflowId = Guid.NewGuid(),
        WorkflowVersion = 1,
        ObjectName = "Product",
        EventName = "Manual",
        RecordId = Guid.NewGuid().ToString(),
        CurrentRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Widget",
            ["Quantity"] = 5,
            ["Notes"] = "",
            ["IsActive"] = true,
            ["ExpiresAt"] = DateTime.Parse("2026-04-28T00:00:00Z")
        }
    };

    private static JsonElement Condition(string json) => JsonDocument.Parse(json).RootElement.Clone();
}