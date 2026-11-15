// Shared by every harness. A harness is a plain console program: it prints one line per check and a
// final summary line that scripts/Verify.ps1 reads. There is no test framework to install.
using System.Globalization;

internal static class Harness
{
    private static int _passed;
    private static int _failed;

    public static void Title(string title) => Console.WriteLine($"=== {title} ===\n");

    public static void Section(string title) => Console.WriteLine($"\n-- {title}");

    public static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $"  -- {detail}")}");
        }
    }

    public static void Equal<T>(string name, T actual, T expected) =>
        Check(name, EqualityComparer<T>.Default.Equals(actual, expected), $"expected {Show(expected)}, got {Show(actual)}");

    /// <summary>True when the action throws the named exception.</summary>
    public static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        catch (Exception) { return false; }
        return false;
    }

    public static async Task<bool> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return true; }
        catch (Exception) { return false; }
        return false;
    }

    /// <summary>Prints the summary line the verifier parses, and returns the process exit code.</summary>
    public static int Finish()
    {
        Console.WriteLine($"\n=== {_passed} passed, {_failed} failed ===");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>The repository root, found by walking up from the harness's own folder.</summary>
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClaimSeeker.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("ClaimSeeker.sln was not found above the harness.");
    }

    public static string Fixtures(params string[] parts) =>
        Path.Combine([RepoRoot(), "tests", "fixtures", .. parts]);

    /// <summary>A fresh folder under the system temp folder, removed by <see cref="TempFolder.Dispose"/>.</summary>
    public static TempFolder NewTempFolder(string label) => new(label);

    private static string Show<T>(T value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder(string label)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"claimseeker-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A clock a harness sets by hand, so nothing it checks depends on when it runs.</summary>
internal sealed class FixedTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public static FixedTime At(string isoUtc) =>
        new(DateTimeOffset.Parse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
}
