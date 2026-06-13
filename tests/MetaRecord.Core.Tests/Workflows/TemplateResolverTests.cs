using System.Text.Json;
using System.Globalization;
using MetaRecord.Workflows.Runtime;
using MetaRecord.Workflows.Runtime.Executors;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class TemplateResolverTests
{
    [Fact]
    public void ResolveTemplate_replaces_current_original_event_and_variable_values()
    {
        var context = CreateContext();
        context.Variables["Status"] = "ready";

        var result = WorkflowValueResolver.ResolveTemplate(
            "{{currentRecord.Title}} changed from {{originalRecord.Priority}} to {{currentRecord.Priority}} during {{event.EventName}} and is {{variable.Status}}.",
            context);

        Assert.Equal("Todo item changed from 12 to 5 during FieldChanged and is ready.", result);
    }

    [Fact]
    public void ResolveTemplateValue_preserves_value_type_when_template_is_only_placeholder()
    {
        var context = CreateContext();

        var value = WorkflowValueResolver.ResolveTemplateValue("{{currentRecord.Id}}", context);

        Assert.IsType<Guid>(value);
        Assert.Equal(context.CurrentRecord["Id"], value);
    }

    [Fact]
    public void ResolveConfiguredValue_resolves_operand_objects()
    {
        var context = CreateContext();
        var operand = Json("""
        {
          "source": "event",
          "field": "RecordId"
        }
        """);

        var value = WorkflowValueResolver.ResolveConfiguredValue(operand, context);

        Assert.Equal(context.RecordId, value);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("42.5", "42.5")]
    public void ResolveConfiguredValue_converts_json_numbers(string json, string expected)
    {
        var value = WorkflowValueResolver.ResolveConfiguredValue(Json(json), CreateContext());

        Assert.Equal(expected, Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ResolveTemplate_throws_clear_error_for_missing_value()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkflowValueResolver.ResolveTemplate("Missing {{currentRecord.DoesNotExist}}", CreateContext()));

        Assert.Contains("currentRecord.DoesNotExist", exception.Message);
    }

    [Fact]
    public void ResolveTemplate_throws_clear_error_for_unsupported_placeholder()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WorkflowValueResolver.ResolveTemplate("Invalid {{currentRecord}}", CreateContext()));

        Assert.Contains("currentRecord", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOperand_supports_case_insensitive_sources()
    {
        var value = WorkflowValueResolver.ResolveOperand(Json("""
        {
          "source": "CURRENTRECORD",
                    "field": "Title"
        }
        """), CreateContext());

                Assert.Equal("Todo item", value);
    }

    private static WorkflowExecutionContext CreateContext()
    {
        var recordId = Guid.NewGuid();
        return new WorkflowExecutionContext
        {
            WorkflowId = Guid.NewGuid(),
            WorkflowVersion = 1,
            ObjectName = "Todo",
            EventName = "FieldChanged",
            RecordId = recordId.ToString(),
            CurrentRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = recordId,
                ["Title"] = "Todo item",
                ["Priority"] = 5,
                ["Description"] = ""
            },
            OriginalRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Priority"] = 12
            },
            ChangedFields = new[] { "Priority" }
        };
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}