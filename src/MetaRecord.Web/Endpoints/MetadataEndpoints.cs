using System.Globalization;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Web.Contracts;
using MetaRecord.Web.Infrastructure;

namespace MetaRecord.Web.Endpoints;

public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metadata");

        group.MapGet("/objects", async (MetadataRepository repository) =>
        {
            var metadata = await repository.LoadAllMetadataAsync();
            return Results.Ok(metadata.Select(ApiMappings.ToResponse).OrderBy(item => item.Name));
        });

        group.MapGet("/objects/{name}", async (string name, MetadataRepository repository) =>
        {
            var metadata = await repository.GetByNameAsync(name);
            if (metadata is null)
                return Results.NotFound();

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        group.MapGet("/objects/{id:guid}", async (Guid id, MetadataRepository repository) =>
        {
            var metadata = await repository.GetByIdAsync(id);
            if (metadata is null)
                return Results.NotFound();

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        group.MapPost("/objects/validate", async (
            ObjectMetadataUpsertRequest request,
            MetadataRepository repository) =>
        {
            var existing = (await repository.LoadAllMetadataAsync()).ToList();
            return Results.Ok(MetadataDefinitionValidator.Validate(request, existing));
        });

        group.MapPost("/objects", async (
            ObjectMetadataUpsertRequest request,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            var existing = (await repository.LoadAllMetadataAsync()).ToList();
            var validation = MetadataDefinitionValidator.Validate(request, existing);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            var id = request.Id is { } requestId && requestId != Guid.Empty ? requestId : Guid.NewGuid();
            if (await repository.GetByIdAsync(id) is not null)
            {
                return Results.Conflict(new MetadataValidationResponse(false, new[]
                {
                    new MetadataValidationIssue(MetadataValidationSeverity.Error, $"Object definition '{id}' already exists.", "id")
                }));
            }

            var metadata = ApiMappings.ToMetadata(request with { Id = id }, id);
            await repository.SaveAsync(metadata);
            await MetadataRegistrySynchronizer.RefreshAsync(repository, entityStore);

            return Results.Created($"/api/metadata/objects/{metadata.Id}", ApiMappings.ToResponse(metadata));
        });

        group.MapPut("/objects/{id:guid}", async (
            Guid id,
            ObjectMetadataUpsertRequest request,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            var existing = await repository.GetByIdAsync(id);
            if (existing is null)
                return Results.NotFound();

            if (request.Id is { } requestId && requestId != Guid.Empty && requestId != id)
            {
                return Results.BadRequest(new MetadataValidationResponse(false, new[]
                {
                    new MetadataValidationIssue(MetadataValidationSeverity.Error, "The request id must match the route id.", "id")
                }));
            }

            var normalizedRequest = request with { Id = id };
            var existingMetadata = (await repository.LoadAllMetadataAsync()).ToList();
            var validation = MetadataDefinitionValidator.Validate(normalizedRequest, existingMetadata, id);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            var metadata = ApiMappings.ToMetadata(normalizedRequest, id);
            await repository.SaveAsync(metadata);
            await MetadataRegistrySynchronizer.RefreshAsync(repository, entityStore);

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        group.MapDelete("/objects/{id:guid}", async (
            Guid id,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            if (!await repository.DeleteAsync(id))
                return Results.NotFound();

            await MetadataRegistrySynchronizer.RefreshAsync(repository, entityStore);
            return Results.NoContent();
        });

        group.MapPost("/objects/{id:guid}/records", async (
            Guid id,
            MetadataRecordSaveRequest request,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            var metadata = await repository.GetByIdAsync(id);
            if (metadata is null)
                return Results.NotFound();

            if (request.Values is null || request.Values.Count == 0)
            {
                return Results.BadRequest(new
                {
                    message = "No record values were provided."
                });
            }

            try
            {
                var values = NormalizeRecordValues(metadata, ApiMappings.ToObjectDictionary(request.Values));
                var recordId = ResolveRecordId(metadata, values);

                await ValidateRelationshipTargetsAsync(metadata, values, repository, entityStore);

                entityStore.EnsureTableExists(metadata);

                var existingValues = entityStore.FindValues(metadata, recordId);
                var isNew = existingValues.Count == 0;
                if (isNew)
                    entityStore.InsertValues(metadata, values);
                else
                    entityStore.UpdateValues(metadata, recordId, values);

                var response = new MetadataRecordSaveResponse(recordId.ToString(), isNew);
                return isNew
                    ? Results.Created($"/api/metadata/objects/{id}/records/{recordId}", response)
                    : Results.Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    message = ex.Message
                });
            }
        });

        group.MapGet("/objects/{id:guid}/records", async (
            Guid id,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            var metadata = await repository.GetByIdAsync(id);
            if (metadata is null)
                return Results.NotFound();

            return Results.Ok(entityStore.AllValues(metadata));
        });

        return app;
    }

    private static Dictionary<string, object?> NormalizeRecordValues(IObjectMetadata metadata, IReadOnlyDictionary<string, object?> rawValues)
    {
        var values = new Dictionary<string, object?>(rawValues, StringComparer.OrdinalIgnoreCase);

        foreach (var property in metadata.Properties)
        {
            if (!values.TryGetValue(property.Name, out var rawValue))
                continue;

            values[property.Name] = NormalizeRecordValue(property, rawValue);
        }

        return values;
    }

    private static object? NormalizeRecordValue(PropertyMetadata property, object? rawValue)
    {
        if (rawValue is null)
            return null;

        var targetType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (targetType == typeof(string))
            return rawValue.ToString();

        var textValue = rawValue.ToString();
        if (string.IsNullOrWhiteSpace(textValue))
            return null;

        if (targetType == typeof(Guid))
        {
            if (rawValue is Guid guidValue)
                return guidValue;

            if (Guid.TryParse(textValue, out var parsedGuid))
                return parsedGuid;

            throw new InvalidOperationException($"The '{property.Name}' field must be a GUID.");
        }

        if (targetType == typeof(bool))
        {
            if (rawValue is bool boolValue)
                return boolValue;

            if (bool.TryParse(textValue, out var parsedBool))
                return parsedBool;

            if (textValue == "1")
                return true;
            if (textValue == "0")
                return false;

            throw new InvalidOperationException($"The '{property.Name}' field must be a boolean.");
        }

        if (targetType == typeof(int))
        {
            if (rawValue is int intValue)
                return intValue;

            if (int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt;

            throw new InvalidOperationException($"The '{property.Name}' field must be an integer.");
        }

        if (targetType == typeof(long))
        {
            if (rawValue is long longValue)
                return longValue;

            if (long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
                return parsedLong;

            throw new InvalidOperationException($"The '{property.Name}' field must be a whole number.");
        }

        if (targetType == typeof(decimal))
        {
            if (rawValue is decimal decimalValue)
                return decimalValue;

            if (decimal.TryParse(textValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal))
                return parsedDecimal;

            throw new InvalidOperationException($"The '{property.Name}' field must be a decimal number.");
        }

        if (targetType == typeof(double))
        {
            if (rawValue is double doubleValue)
                return doubleValue;

            if (double.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedDouble))
                return parsedDouble;

            throw new InvalidOperationException($"The '{property.Name}' field must be a number.");
        }

        if (targetType == typeof(DateTime))
        {
            if (rawValue is DateTime dateTimeValue)
                return dateTimeValue;

            if (DateTime.TryParse(textValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDateTime))
                return parsedDateTime;

            throw new InvalidOperationException($"The '{property.Name}' field must be a valid date and time value.");
        }

        return rawValue;
    }

    private static async Task ValidateRelationshipTargetsAsync(
        IObjectMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        MetadataRepository repository,
        EntityStore entityStore)
    {
        if (metadata.Relationships.Count == 0)
            return;

        var metadataById = (await repository.LoadAllMetadataAsync()).ToDictionary(item => item.Id);
        var currentObjectId = metadata.Id;

        foreach (var relationship in metadata.Relationships)
        {
            if (!values.TryGetValue(relationship.SourcePropertyName, out var rawValue) || rawValue is null)
                continue;

            if (rawValue is Guid guidValue && guidValue == Guid.Empty)
                continue;

            if (relationship.TargetObjectId == currentObjectId)
                throw new InvalidOperationException($"Relationship '{relationship.Name}' cannot target the same object it belongs to.");

            if (!metadataById.TryGetValue(relationship.TargetObjectId, out var targetMetadata))
                throw new InvalidOperationException($"Relationship '{relationship.Name}' targets an unknown object.");

            var targetPropertyName = string.IsNullOrWhiteSpace(relationship.TargetPropertyName) ? "Id" : relationship.TargetPropertyName;
            var targetProperty = targetMetadata.Properties.FirstOrDefault(property => string.Equals(property.Name, targetPropertyName, StringComparison.OrdinalIgnoreCase));

            if (targetProperty is null)
                throw new InvalidOperationException($"Relationship '{relationship.Name}' targets missing property '{targetPropertyName}' on object '{targetMetadata.Name}'.");

            var targetRecord = entityStore.FindValuesByColumn(targetMetadata, targetProperty.ColumnName, rawValue);
            if (targetRecord is null || targetRecord.Count == 0)
                throw new InvalidOperationException($"Relationship '{relationship.Name}' points to a missing '{targetMetadata.Name}' record.");
        }
    }

    private static Guid ResolveRecordId(IObjectMetadata metadata, IDictionary<string, object?> values)
    {
        var keyProperty = GetPrimaryKeyProperty(metadata);
        var keyName = keyProperty?.Name ?? "Id";

        if (!values.TryGetValue(keyName, out var rawValue) || rawValue is null)
        {
            var generatedId = Guid.NewGuid();
            values[keyName] = generatedId;
            return generatedId;
        }

        if (rawValue is Guid guidValue)
        {
            values[keyName] = guidValue;
            return guidValue;
        }

        if (Guid.TryParse(rawValue.ToString(), out var parsedId))
        {
            values[keyName] = parsedId;
            return parsedId;
        }

        throw new InvalidOperationException($"The '{keyName}' field must be a GUID.");
    }

    private static PropertyMetadata? GetPrimaryKeyProperty(IObjectMetadata metadata) =>
        metadata.Properties.FirstOrDefault(property => property.IsPrimaryKey) ??
        metadata.Properties.FirstOrDefault(property => property.Name == "Id");
}