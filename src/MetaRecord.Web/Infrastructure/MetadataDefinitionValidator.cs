using System.Text.RegularExpressions;
using MetaRecord.Models;
using MetaRecord.Web.Contracts;

namespace MetaRecord.Web.Infrastructure;

internal static class MetadataDefinitionValidator
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static MetadataValidationResponse Validate(
        ObjectMetadataUpsertRequest request,
        IReadOnlyCollection<IObjectMetadata> existingMetadata,
        Guid? currentId = null)
    {
        var issues = new List<MetadataValidationIssue>();

        if (request is null)
        {
            issues.Add(Error("A metadata object definition is required."));
            return new MetadataValidationResponse(false, issues);
        }

        ValidateName(request.Name, issues);
        ValidateTableName(request.TableName, issues);
        ValidateProperties(request.Properties, issues);
        ValidateRelationships(request, existingMetadata, issues);

        var effectiveId = currentId ?? request.Id;
        var existingByName = existingMetadata.FirstOrDefault(metadata => string.Equals(metadata.Name, request.Name, StringComparison.OrdinalIgnoreCase));
        if (existingByName is not null && (effectiveId is null || existingByName.Id != effectiveId))
            issues.Add(Error($"Object '{request.Name}' already exists.", "name"));

        var existingByTable = existingMetadata.FirstOrDefault(metadata => string.Equals(metadata.TableName, request.TableName, StringComparison.OrdinalIgnoreCase));
        if (existingByTable is not null && (effectiveId is null || existingByTable.Id != effectiveId))
            issues.Add(Error($"Table '{request.TableName}' already exists.", "tableName"));

        return new MetadataValidationResponse(issues.All(issue => issue.Severity != MetadataValidationSeverity.Error), issues);
    }

    private static void ValidateName(string? name, List<MetadataValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(Error("Object name is required.", "name"));
            return;
        }

        if (!IdentifierPattern.IsMatch(name))
            issues.Add(Error("Object name must start with a letter or underscore and contain only letters, digits, or underscores.", "name"));
    }

    private static void ValidateTableName(string? tableName, List<MetadataValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            issues.Add(Error("Table name is required.", "tableName"));
            return;
        }

        if (!IdentifierPattern.IsMatch(tableName))
            issues.Add(Error("Table name must start with a letter or underscore and contain only letters, digits, or underscores.", "tableName"));
    }

    private static void ValidateProperties(IReadOnlyList<PropertyMetadataUpsertRequest>? properties, List<MetadataValidationIssue> issues)
    {
        if (properties is null || properties.Count == 0)
        {
            issues.Add(Error("At least one property is required.", "properties"));
            return;
        }

        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasIdProperty = false;
        var primaryKeyCount = 0;

        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var fieldPrefix = $"properties[{index}]";

            if (string.IsNullOrWhiteSpace(property.Name))
            {
                issues.Add(Error("Property name is required.", $"{fieldPrefix}.name"));
                continue;
            }

            if (!IdentifierPattern.IsMatch(property.Name))
                issues.Add(Error("Property name must start with a letter or underscore and contain only letters, digits, or underscores.", $"{fieldPrefix}.name"));

            if (!propertyNames.Add(property.Name))
                issues.Add(Error($"Property name '{property.Name}' is duplicated.", $"{fieldPrefix}.name"));

            if (string.IsNullOrWhiteSpace(property.ColumnName))
            {
                issues.Add(Error("Column name is required.", $"{fieldPrefix}.columnName"));
            }
            else
            {
                if (!IdentifierPattern.IsMatch(property.ColumnName))
                    issues.Add(Error("Column name must start with a letter or underscore and contain only letters, digits, or underscores.", $"{fieldPrefix}.columnName"));

                if (!columnNames.Add(property.ColumnName))
                    issues.Add(Error($"Column name '{property.ColumnName}' is duplicated.", $"{fieldPrefix}.columnName"));
            }

            if (string.IsNullOrWhiteSpace(property.ClrType))
            {
                issues.Add(Error("CLR type is required.", $"{fieldPrefix}.clrType"));
            }
            else if (!MetadataTypeMapper.TryParseClrType(property.ClrType, out _))
            {
                issues.Add(Error($"CLR type '{property.ClrType}' is not supported.", $"{fieldPrefix}.clrType"));
            }

            if (property.MaxLength is < 1)
                issues.Add(Error("Maximum length must be greater than zero.", $"{fieldPrefix}.maxLength"));

            if (property.IsPrimaryKey)
                primaryKeyCount++;

            if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                hasIdProperty = true;

                if (!string.Equals(property.ColumnName, "Id", StringComparison.OrdinalIgnoreCase))
                    issues.Add(Error("The Id property must map to the Id column.", $"{fieldPrefix}.columnName"));

                if (!string.Equals(property.ClrType, "Guid", StringComparison.OrdinalIgnoreCase))
                    issues.Add(Error("The Id property must use the Guid CLR type.", $"{fieldPrefix}.clrType"));
            }
        }

        if (!hasIdProperty)
            issues.Add(Error("An Id property is required.", "properties"));

        if (primaryKeyCount > 1)
            issues.Add(Error("Only one property can be marked as the primary key.", "properties"));
    }

    private static void ValidateRelationships(
        ObjectMetadataUpsertRequest request,
        IReadOnlyCollection<IObjectMetadata> existingMetadata,
        List<MetadataValidationIssue> issues)
    {
        if (request.Relationships is null || request.Relationships.Count == 0)
            return;

        var propertyLookup = request.Properties
            .Where(property => !string.IsNullOrWhiteSpace(property.Name))
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var relationshipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentObjectId = request.Id;
        var objectLookup = existingMetadata.ToDictionary(metadata => metadata.Id);
        if (currentObjectId is { } objectId)
            objectLookup[objectId] = BuildDraftObjectMetadata(request);

        for (var index = 0; index < request.Relationships.Count; index++)
        {
            var relationship = request.Relationships[index];
            var fieldPrefix = $"relationships[{index}]";

            if (string.IsNullOrWhiteSpace(relationship.Name))
            {
                issues.Add(Error("Relationship name is required.", $"{fieldPrefix}.name"));
                continue;
            }

            if (!IdentifierPattern.IsMatch(relationship.Name))
                issues.Add(Error("Relationship name must start with a letter or underscore and contain only letters, digits, or underscores.", $"{fieldPrefix}.name"));

            if (!relationshipNames.Add(relationship.Name))
                issues.Add(Error($"Relationship name '{relationship.Name}' is duplicated.", $"{fieldPrefix}.name"));

            if (string.IsNullOrWhiteSpace(relationship.SourcePropertyName))
            {
                issues.Add(Error("Source property name is required.", $"{fieldPrefix}.sourcePropertyName"));
            }
            else
            {
                if (!sourceProperties.Add(relationship.SourcePropertyName))
                    issues.Add(Error($"Source property '{relationship.SourcePropertyName}' is already used by another relationship.", $"{fieldPrefix}.sourcePropertyName"));

                if (!propertyLookup.TryGetValue(relationship.SourcePropertyName, out var sourceProperty))
                {
                    issues.Add(Error($"Source property '{relationship.SourcePropertyName}' was not found.", $"{fieldPrefix}.sourcePropertyName"));
                }
                else if (!string.Equals(sourceProperty.ClrType, "Guid", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Error($"Source property '{relationship.SourcePropertyName}' must use the Guid CLR type.", $"{fieldPrefix}.sourcePropertyName"));
                }
            }

            if (relationship.TargetObjectId == Guid.Empty)
            {
                issues.Add(Error("Target object is required.", $"{fieldPrefix}.targetObjectId"));
                continue;
            }

            if (currentObjectId is { } selfObjectId && relationship.TargetObjectId == selfObjectId)
            {
                issues.Add(Error("Self-referential relationships are not supported yet.", $"{fieldPrefix}.targetObjectId"));
                continue;
            }

            if (!objectLookup.TryGetValue(relationship.TargetObjectId, out var targetObject))
            {
                issues.Add(Error($"Target object '{relationship.TargetObjectId}' was not found.", $"{fieldPrefix}.targetObjectId"));
                continue;
            }

            var targetPropertyName = string.IsNullOrWhiteSpace(relationship.TargetPropertyName) ? "Id" : relationship.TargetPropertyName;
            var targetProperty = targetObject.Properties.FirstOrDefault(property => string.Equals(property.Name, targetPropertyName, StringComparison.OrdinalIgnoreCase));

            if (targetProperty is null)
            {
                issues.Add(Error($"Target property '{targetPropertyName}' was not found on object '{relationship.TargetObjectName}'.", $"{fieldPrefix}.targetPropertyName"));
            }
            else if (targetProperty.ClrType != typeof(Guid))
            {
                issues.Add(Error($"Target property '{targetPropertyName}' on object '{relationship.TargetObjectName}' must use the Guid CLR type.", $"{fieldPrefix}.targetPropertyName"));
            }

            if (relationship.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
            {
                issues.Add(Error($"Relationship cardinality '{relationship.Cardinality}' is not supported yet.", $"{fieldPrefix}.cardinality"));
            }

            if (relationship.DeleteBehavior == RelationshipDeleteBehavior.SetNull &&
                propertyLookup.TryGetValue(relationship.SourcePropertyName, out var nullableSourceProperty) &&
                nullableSourceProperty.IsRequired)
            {
                issues.Add(Error($"Relationship '{relationship.Name}' cannot use SetNull when the source property is required.", $"{fieldPrefix}.deleteBehavior"));
            }
        }
    }

    private static IObjectMetadata BuildDraftObjectMetadata(ObjectMetadataUpsertRequest request)
    {
        return new ObjectMetadata
        {
            Name = request.Name,
            TableName = request.TableName,
            Properties = (request.Properties ?? Array.Empty<PropertyMetadataUpsertRequest>())
                .Select(property => new PropertyMetadata(
                    property.Name,
                    property.ColumnName,
                    MetadataTypeMapper.ParseClrType(property.ClrType),
                    property.IsRequired)
                {
                    MaxLength = property.MaxLength,
                    IsUnique = property.IsUnique,
                    IsPrimaryKey = property.IsPrimaryKey,
                    DefaultValue = property.DefaultValue,
                    Caption = property.Caption
                })
                .ToList(),
            Relationships = Array.Empty<RelationshipMetadata>()
        };
    }

    private static MetadataValidationIssue Error(string message, string? field = null) =>
        new(MetadataValidationSeverity.Error, message, field);
}