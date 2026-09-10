using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlServerAdvisor.Worker.Services;

internal sealed record WorkloadSqlAnalysis(
    string? DatabaseName,
    string NormalizedHash,
    string ReferencedObjects,
    string ReferencedColumns,
    string ParseMessage);

internal static class WorkloadSqlAnalyzer
{
    private static readonly Regex BlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@"--[^\r\n]*", RegexOptions.Compiled);
    private static readonly Regex UseRegex = new(@"\bUSE\s+(?<db>\[[^\]]+\]|[A-Za-z0-9_$#@.\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableRegex = new(
        @"\b(?:FROM|JOIN|UPDATE|INTO|MERGE\s+INTO|DELETE\s+FROM)\s+(?<object>(?:\[[^\]]+\]|[#A-Za-z0-9_$]+)(?:\s*\.\s*(?:\[[^\]]+\]|[#A-Za-z0-9_$]+)){0,2})(?:\s+(?:AS\s+)?(?<alias>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$]*))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QualifiedColumnRegex = new(
        @"(?<alias>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$]*)\s*\.\s*(?<column>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$]*)",
        RegexOptions.Compiled);
    private static readonly Regex StringLiteralRegex = new(@"N?'(?:''|[^'])*'", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberLiteralRegex = new(@"(?<![A-Za-z0-9_])[-+]?\d+(?:\.\d+)?(?![A-Za-z0-9_])", RegexOptions.Compiled);
    private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "ON", "GROUP", "ORDER", "HAVING",
        "UNION", "EXCEPT", "INTERSECT", "OPTION", "WITH", "SET", "VALUES", "OUTPUT", "WHEN", "USING"
    };

    public static WorkloadSqlAnalysis Analyze(string sql, string? defaultDatabase)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new WorkloadSqlAnalysis(defaultDatabase, Hash(string.Empty), string.Empty, string.Empty, "Dosya boş.");

        var withoutComments = LineCommentRegex.Replace(BlockCommentRegex.Replace(sql, " "), " ");
        var useMatch = UseRegex.Match(withoutComments);
        var databaseName = useMatch.Success ? CleanIdentifier(useMatch.Groups["db"].Value) : NormalizeNullable(defaultDatabase);

        var referencedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in TableRegex.Matches(withoutComments))
        {
            var objectName = NormalizeObjectName(match.Groups["object"].Value);
            if (string.IsNullOrWhiteSpace(objectName) || objectName.StartsWith('#'))
                continue;

            referencedObjects.Add(objectName);

            var alias = CleanIdentifier(match.Groups["alias"].Value);
            if (!string.IsNullOrWhiteSpace(alias) && !SqlKeywords.Contains(alias))
                aliasMap[alias] = objectName;

            var parts = objectName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
                aliasMap.TryAdd(parts[^1], objectName);
        }

        var columnsByObject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in QualifiedColumnRegex.Matches(withoutComments))
        {
            var alias = CleanIdentifier(match.Groups["alias"].Value);
            var column = CleanIdentifier(match.Groups["column"].Value);
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(column))
                continue;
            if (!aliasMap.TryGetValue(alias, out var objectName))
                continue;

            if (!columnsByObject.TryGetValue(objectName, out var columns))
            {
                columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                columnsByObject[objectName] = columns;
            }
            columns.Add(column);
        }

        var normalized = NormalizeForHash(withoutComments);
        var referencedColumns = string.Join("; ", columnsByObject
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}: {string.Join(", ", x.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))}"));

        var parseMessage = referencedObjects.Count == 0
            ? "Tablo referansı çıkarılamadı; dinamik SQL, CTE veya alışılmadık sözdizimi olabilir. Dosya yine AI bağlamında kullanılabilir."
            : $"{referencedObjects.Count} nesne ve {columnsByObject.Sum(x => x.Value.Count)} nitelikli kolon referansı çıkarıldı. Regex tabanlı konservatif analizdir.";

        return new WorkloadSqlAnalysis(
            databaseName,
            Hash(normalized),
            string.Join("; ", referencedObjects.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            referencedColumns,
            parseMessage);
    }

    public static string ContentHash(string sql) => Hash(sql);

    private static string NormalizeForHash(string value)
    {
        var withoutStrings = StringLiteralRegex.Replace(value, "?");
        var withoutNumbers = NumberLiteralRegex.Replace(withoutStrings, "?");
        return WhiteSpaceRegex.Replace(withoutNumbers, " ").Trim().ToUpperInvariant();
    }

    private static string NormalizeObjectName(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanIdentifier)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        return string.Join('.', parts);
    }

    private static string CleanIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Trim().Trim('[', ']', '"');
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
