namespace QueueManager.Tests;

/// <summary>Helpers for giving each test host its own throwaway SQLite database.</summary>
internal static class TestDb
{
    public const string ConnectionStringKey = "ConnectionStrings:QueueDb";

    public static string NewConnectionString() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"queuemanager-tests-{Guid.NewGuid():N}.db")}";
}
