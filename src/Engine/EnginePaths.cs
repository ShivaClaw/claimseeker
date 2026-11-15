namespace ClaimSeeker.Engine;

/// <summary>
/// Where the engine keeps everything, named once. The workspace is a folder in the person's own
/// profile: it holds names and addresses, so it is never created anywhere shared, and never inside
/// a source checkout unless someone points it there on purpose.
/// </summary>
internal static class EnginePaths
{
    public const string WorkspaceFolderName = "ClaimSeeker";
    public const string WorkspaceVariable = "CLAIMSEEKER_WORKSPACE";

    /// <summary>A file that marks a folder as a ClaimSeeker workspace. Delete-all-data refuses a folder without it.</summary>
    public const string Marker = "claimseeker.workspace";
    public const string Database = "claimseeker.db";
    public const string LockFile = "engine.lock";
    public const string Packets = "packets";
    public const string Drafts = "drafts";
    public const string Reports = "reports";
    public const string ExampleHousehold = "household.example.json";

    /// <summary>
    /// The per-user data folder on each platform: %LOCALAPPDATA% on Windows, ~/Library/Application
    /// Support on macOS, the XDG data folder elsewhere.
    /// </summary>
    public static string DefaultWorkspace() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WorkspaceFolderName);

    /// <summary>An explicit flag wins, then the environment variable, then the per-user default.</summary>
    public static string Resolve(string? flag, string? fallback)
    {
        var chosen = !string.IsNullOrWhiteSpace(flag) ? flag
            : Environment.GetEnvironmentVariable(WorkspaceVariable) is { Length: > 0 } fromEnvironment ? fromEnvironment
            : fallback ?? DefaultWorkspace();
        return Path.GetFullPath(chosen);
    }
}

/// <summary>
/// Holds the workspace for one process. A second engine on the same workspace is refused instead of
/// being allowed to interleave its writes with the first.
/// </summary>
internal sealed class WorkspaceLock : IDisposable
{
    private readonly FileStream _stream;

    private WorkspaceLock(FileStream stream) => _stream = stream;

    public static WorkspaceLock Acquire(string workspace)
    {
        var path = Path.Combine(workspace, EnginePaths.LockFile);
        try
        {
            return new WorkspaceLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            throw new EngineUsageException("Another ClaimSeeker process is using this workspace. Wait for it to finish, or stop it, and try again.");
        }
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>Something the person can fix: a missing file, a wrong flag, a step run out of order.</summary>
internal sealed class EngineUsageException(string message) : Exception(message);
