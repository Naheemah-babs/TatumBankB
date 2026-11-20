using Microsoft.Data.SqlClient;

namespace TatumWelcome2.Infrastructure.Database;

internal static class SqlServerErrors
{
    public const int UniqueConstraint = 2627;
    public const int UniqueIndex = 2601;

    public static bool IsUniqueViolation(SqlException ex) =>
        ex.Number is UniqueConstraint or UniqueIndex;

    public static bool IsUniqueViolation(SqlException ex, string keyName) =>
        IsUniqueViolation(ex) &&
        ex.Message.Contains(keyName, StringComparison.OrdinalIgnoreCase);
}