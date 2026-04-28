using MetaRecord.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;

namespace MetaRecord.Data;

/// <summary>
/// Entity storage using SQLite with metadata-driven SQL generation.
/// Uses raw SQL based on metadata definitions rather than EF Core entity tracking.
/// </summary>
public class EntityStore
{
    private static EntityStore? _current;
    public static EntityStore Current => _current ??= new EntityStore();

    private readonly string _connectionString;

    public EntityStore()
    {
        var folder = Environment.CurrentDirectory;
        var dbPath = Path.Join(folder, "metarecord.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public EntityStore(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path is required.", nameof(dbPath));

        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Ensures the entity table exists based on metadata.
    /// Honors <see cref="PropertyMetadata.MaxLength"/>, <see cref="PropertyMetadata.IsUnique"/>,
    /// <see cref="PropertyMetadata.IsPrimaryKey"/>, and <see cref="PropertyMetadata.DefaultValue"/>.
    /// </summary>
    public void EnsureTableExists(IObjectMetadata metadata)
    {
        var pkProp = metadata.Properties.FirstOrDefault(p => p.IsPrimaryKey)
                     ?? metadata.Properties.FirstOrDefault(p => p.Name == "Id");

        var columns = new List<string>();
        foreach (var prop in metadata.Properties)
        {
            var sqlType = GetSqliteType(prop.ClrType, prop.MaxLength);
            var parts = new List<string> { prop.ColumnName, sqlType };

            if (prop == pkProp)
                parts.Add("PRIMARY KEY");
            if (prop.IsRequired && prop != pkProp)
                parts.Add("NOT NULL");
            if (prop.IsUnique && prop != pkProp)
                parts.Add("UNIQUE");
            if (!string.IsNullOrWhiteSpace(prop.DefaultValue))
                parts.Add($"DEFAULT {prop.DefaultValue}");

            columns.Add(string.Join(" ", parts));
        }

        // Fallback when metadata has no Id and no property marked primary key.
        if (pkProp == null)
            columns.Insert(0, "Id TEXT PRIMARY KEY");

        var createSql = $"CREATE TABLE IF NOT EXISTS {metadata.TableName} ({string.Join(", ", columns)})";
        ExecuteNonQuery(createSql);
    }

    /// <summary>
    /// Inserts an entity using metadata-driven SQL.
    /// </summary>
    public void Insert<T>(T entity, IObjectMetadata metadata) where T : class
    {
        var columns = new List<string>();
        var parameters = new List<string>();
        var values = new List<SqliteParameter>();

        foreach (var prop in metadata.Properties)
        {
            var propertyInfo = typeof(T).GetProperty(prop.Name);
            if (propertyInfo != null)
            {
                columns.Add(prop.ColumnName);
                parameters.Add($"@{prop.Name}");
                var value = propertyInfo.GetValue(entity);
                // Convert Guid to string for SQLite TEXT storage
                if (value is Guid guidValue)
                    value = guidValue.ToString();
                values.Add(new SqliteParameter($"@{prop.Name}", value ?? DBNull.Value));
            }
        }

        var sql = $"INSERT INTO {metadata.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
        ExecuteNonQuery(sql, values.ToArray());
    }

    public void InsertValues(IObjectMetadata metadata, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);

        var valueLookup = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        var primaryKey = GetPrimaryKeyProperty(metadata);
        if (primaryKey is not null && !valueLookup.ContainsKey(primaryKey.Name) && primaryKey.ClrType == typeof(Guid))
            valueLookup[primaryKey.Name] = Guid.NewGuid();
        if (primaryKey is null && !valueLookup.ContainsKey("Id"))
            valueLookup["Id"] = Guid.NewGuid();

        ValidateValueFields(metadata, valueLookup.Keys, allowImplicitId: primaryKey is null);

        var columns = new List<string>();
        var parameters = new List<string>();
        var sqlParameters = new List<SqliteParameter>();

        if (primaryKey is null && valueLookup.TryGetValue("Id", out var implicitId))
        {
            AddParameter("Id", implicitId, columns, parameters, sqlParameters);
        }

        foreach (var property in metadata.Properties)
        {
            if (!valueLookup.TryGetValue(property.Name, out var value))
                continue;

            AddParameter(property.ColumnName, value, columns, parameters, sqlParameters);
        }

        if (columns.Count == 0)
            throw new InvalidOperationException($"No values were supplied for object '{metadata.Name}'.");

        var sql = $"INSERT INTO {metadata.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
        ExecuteNonQuery(sql, sqlParameters.ToArray());
    }

    public Dictionary<string, object?> FindValues(IObjectMetadata metadata, Guid id)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var keyColumn = GetPrimaryKeyColumnName(metadata);
        var sql = $"SELECT * FROM {metadata.TableName} WHERE {keyColumn} = @Id";

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.Add(new SqliteParameter("@Id", id.ToString()));

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? MapToValues(reader, metadata)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateValues(IObjectMetadata metadata, Guid id, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);

        var primaryKey = GetPrimaryKeyProperty(metadata);
        ValidateValueFields(metadata, values.Keys, allowImplicitId: primaryKey is null);

        var keyPropertyName = primaryKey?.Name ?? "Id";
        var keyColumn = primaryKey?.ColumnName ?? "Id";
        var setClauses = new List<string>();
        var sqlParameters = new List<SqliteParameter>();

        foreach (var property in metadata.Properties)
        {
            if (string.Equals(property.Name, keyPropertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!values.TryGetValue(property.Name, out var value))
                continue;

            var parameterName = $"@p{sqlParameters.Count}";
            setClauses.Add($"{property.ColumnName} = {parameterName}");
            sqlParameters.Add(new SqliteParameter(parameterName, ConvertToSqliteValue(value) ?? DBNull.Value));
        }

        if (setClauses.Count == 0)
            throw new InvalidOperationException($"No updatable values were supplied for object '{metadata.Name}'.");

        sqlParameters.Add(new SqliteParameter("@Id", id.ToString()));
        var sql = $"UPDATE {metadata.TableName} SET {string.Join(", ", setClauses)} WHERE {keyColumn} = @Id";
        ExecuteNonQuery(sql, sqlParameters.ToArray());
    }

    /// <summary>
    /// Updates an entity using metadata-driven SQL.
    /// </summary>
    public void Update<T>(T entity, IObjectMetadata metadata, Guid id) where T : class
    {
        var setClauses = new List<string>();
        var values = new List<SqliteParameter>();

        foreach (var prop in metadata.Properties.Where(p => p.Name != "Id"))
        {
            var propertyInfo = typeof(T).GetProperty(prop.Name);
            if (propertyInfo != null)
            {
                setClauses.Add($"{prop.ColumnName} = @{prop.Name}");
                var value = propertyInfo.GetValue(entity);
                // Convert Guid to string for SQLite TEXT storage
                if (value is Guid guidValue)
                    value = guidValue.ToString();
                values.Add(new SqliteParameter($"@{prop.Name}", value ?? DBNull.Value));
            }
        }

        values.Add(new SqliteParameter("@Id", id.ToString()));
        var sql = $"UPDATE {metadata.TableName} SET {string.Join(", ", setClauses)} WHERE Id = @Id";
        ExecuteNonQuery(sql, values.ToArray());
    }

    /// <summary>
    /// Deletes an entity by ID.
    /// </summary>
    public void Delete(IObjectMetadata metadata, Guid id)
    {
        var sql = $"DELETE FROM {metadata.TableName} WHERE Id = @Id";
        ExecuteNonQuery(sql, new SqliteParameter("@Id", id.ToString()));
    }

    /// <summary>
    /// Finds an entity by ID using metadata-driven SQL.
    /// </summary>
    public T? Find<T>(IObjectMetadata metadata, Guid id) where T : class, new()
    {
        var sql = $"SELECT * FROM {metadata.TableName} WHERE Id = @Id";
        
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.Add(new SqliteParameter("@Id", id.ToString()));
        
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return MapToEntity<T>(reader, metadata);
        }
        return null;
    }

    /// <summary>
    /// Returns all entities of a type.
    /// </summary>
    public List<T> All<T>(IObjectMetadata metadata) where T : class, new()
    {
        var sql = $"SELECT * FROM {metadata.TableName}";
        var results = new List<T>();
        
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();
        
        while (reader.Read())
        {
            results.Add(MapToEntity<T>(reader, metadata));
        }
        return results;
    }

    /// <summary>
    /// Counts all entities of a type.
    /// </summary>
    public int Count(IObjectMetadata metadata)
    {
        var sql = $"SELECT COUNT(*) FROM {metadata.TableName}";
        
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private T MapToEntity<T>(SqliteDataReader reader, IObjectMetadata metadata) where T : class, new()
    {
        var entity = new T();
        
        foreach (var prop in metadata.Properties)
        {
            var propertyInfo = typeof(T).GetProperty(prop.Name);
            if (propertyInfo != null)
            {
                var ordinal = reader.GetOrdinal(prop.ColumnName);
                if (!reader.IsDBNull(ordinal))
                {
                    var value = reader.GetValue(ordinal);
                    var convertedValue = ConvertValue(value, prop.ClrType);
                    propertyInfo.SetValue(entity, convertedValue);
                }
            }
        }
        return entity;
    }

    private Dictionary<string, object?> MapToValues(SqliteDataReader reader, IObjectMetadata metadata)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (GetPrimaryKeyProperty(metadata) is null && TryGetOrdinal(reader, "Id", out var implicitIdOrdinal))
        {
            values["Id"] = reader.IsDBNull(implicitIdOrdinal)
                ? null
                : ConvertValue(reader.GetValue(implicitIdOrdinal), typeof(Guid));
        }

        foreach (var prop in metadata.Properties)
        {
            var ordinal = reader.GetOrdinal(prop.ColumnName);
            values[prop.Name] = reader.IsDBNull(ordinal)
                ? null
                : ConvertValue(reader.GetValue(ordinal), prop.ClrType);
        }

        return values;
    }

    private object? ConvertValue(object value, Type targetType)
    {
        if (value == DBNull.Value) return null;
        
        if (targetType == typeof(Guid))
            return Guid.Parse(value.ToString()!);
        if (targetType == typeof(decimal))
            return Convert.ToDecimal(value);
        if (targetType == typeof(int))
            return Convert.ToInt32(value);
        if (targetType == typeof(long))
            return Convert.ToInt64(value);
        if (targetType == typeof(double))
            return Convert.ToDouble(value);
        if (targetType == typeof(float))
            return Convert.ToSingle(value);
        if (targetType == typeof(bool))
            return Convert.ToBoolean(value);
        if (targetType == typeof(DateTime))
            return DateTime.Parse(value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            
        return value;
    }

    private void ExecuteNonQuery(string sql, params SqliteParameter[] parameters)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        command.ExecuteNonQuery();
    }

    private static string GetSqliteType(Type clrType, int? maxLength = null) => clrType.Name switch
    {
        "String" => maxLength is > 0 ? $"TEXT({maxLength})" : "TEXT",
        "Int32" or "Int64" => "INTEGER",
        "Decimal" or "Double" or "Single" => "REAL",
        "Boolean" => "INTEGER",
        "DateTime" => "TEXT",
        "Guid" => "TEXT",
        _ => "TEXT"
    };

    private static PropertyMetadata? GetPrimaryKeyProperty(IObjectMetadata metadata) =>
        metadata.Properties.FirstOrDefault(property => property.IsPrimaryKey) ??
        metadata.Properties.FirstOrDefault(property => property.Name == "Id");

    private static string GetPrimaryKeyColumnName(IObjectMetadata metadata) =>
        GetPrimaryKeyProperty(metadata)?.ColumnName ?? "Id";

    private static void ValidateValueFields(
        IObjectMetadata metadata,
        IEnumerable<string> fieldNames,
        bool allowImplicitId)
    {
        var knownProperties = metadata.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fieldName in fieldNames)
        {
            if (knownProperties.Contains(fieldName))
                continue;
            if (allowImplicitId && string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            throw new InvalidOperationException($"Field '{fieldName}' does not exist on object '{metadata.Name}'.");
        }
    }

    private static void AddParameter(
        string columnName,
        object? value,
        List<string> columns,
        List<string> parameters,
        List<SqliteParameter> sqlParameters)
    {
        var parameterName = $"@p{sqlParameters.Count}";
        columns.Add(columnName);
        parameters.Add(parameterName);
        sqlParameters.Add(new SqliteParameter(parameterName, ConvertToSqliteValue(value) ?? DBNull.Value));
    }

    private static bool TryGetOrdinal(SqliteDataReader reader, string columnName, out int ordinal)
    {
        try
        {
            ordinal = reader.GetOrdinal(columnName);
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            ordinal = -1;
            return false;
        }
    }

    private static object? ConvertToSqliteValue(object? value) => value switch
    {
        null => null,
        Guid guidValue => guidValue.ToString(),
        DateTime dateTimeValue => dateTimeValue.ToString("O"),
        _ => value
    };

    public static void Reset() => _current = new EntityStore();
}
