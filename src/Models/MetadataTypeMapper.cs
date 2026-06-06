namespace MetaRecord.Models;

public static class MetadataTypeMapper
{
    public static bool TryParseClrType(string typeName, out Type clrType)
    {
        clrType = typeName switch
        {
            "Guid" => typeof(Guid),
            "String" => typeof(string),
            "Int32" => typeof(int),
            "Int64" => typeof(long),
            "Decimal" => typeof(decimal),
            "Double" => typeof(double),
            "Boolean" => typeof(bool),
            "DateTime" => typeof(DateTime),
            _ => typeof(string)
        };

        return clrType != typeof(string) || string.Equals(typeName, "String", StringComparison.Ordinal);
    }

    public static Type ParseClrType(string typeName) => typeName switch
    {
        "Guid" => typeof(Guid),
        "String" => typeof(string),
        "Int32" => typeof(int),
        "Int64" => typeof(long),
        "Decimal" => typeof(decimal),
        "Double" => typeof(double),
        "Boolean" => typeof(bool),
        "DateTime" => typeof(DateTime),
        _ => throw new ArgumentOutOfRangeException(nameof(typeName), typeName, "Unsupported CLR type name.")
    };
}