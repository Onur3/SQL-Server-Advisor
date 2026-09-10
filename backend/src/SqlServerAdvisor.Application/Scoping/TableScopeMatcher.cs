using SqlServerAdvisor.Application.DTOs;

namespace SqlServerAdvisor.Application.Scoping;

public static class TableScopeMatcher
{
    public static bool Allows(
        IReadOnlyCollection<MonitoredTableScopeItemDto> scope,
        Guid serverProfileId,
        string? databaseName,
        string? objectName)
    {
        var serverScope = scope.Where(x => x.ServerProfileId == serverProfileId).ToArray();
        if (serverScope.Length == 0)
            return true;

        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(objectName))
            return false;

        var (schemaName, tableName) = ParseObjectName(objectName);
        return serverScope.Any(x =>
            x.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase) &&
            x.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(schemaName) || x.SchemaName.Equals(schemaName, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool Allows(
        IReadOnlyCollection<MonitoredTableSelectionDto> scope,
        string? databaseName,
        string? objectName)
    {
        if (scope.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(objectName))
            return false;

        var (schemaName, tableName) = ParseObjectName(objectName);
        return scope.Any(x =>
            x.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase) &&
            x.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(schemaName) || x.SchemaName.Equals(schemaName, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool DatabaseIsInScope(IReadOnlyCollection<MonitoredTableSelectionDto> scope, string databaseName) =>
        scope.Count == 0 || scope.Any(x => x.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase));

    public static (string SchemaName, string TableName) ParseObjectName(string objectName)
    {
        var parts = objectName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeIdentifier)
            .Where(x => x.Length > 0)
            .ToArray();

        return parts.Length switch
        {
            >= 2 => (parts[^2], parts[^1]),
            1 => (string.Empty, parts[0]),
            _ => (string.Empty, string.Empty)
        };
    }

    private static string NormalizeIdentifier(string value) =>
        value.Trim().Trim('[', ']', '"', '`');
}
