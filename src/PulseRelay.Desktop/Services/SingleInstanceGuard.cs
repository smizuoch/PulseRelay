namespace PulseRelay.Desktop.Services;

/// <summary>Owns the application-wide lock that prevents duplicate desktop instances.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string ApplicationLockName = "PulseRelay.Desktop.SingleInstance";

    private readonly FileStream? _lockFile;
    private bool _disposed;

    private SingleInstanceGuard(FileStream? lockFile, bool hasOwnership)
    {
        _lockFile = lockFile;
        HasOwnership = hasOwnership;
    }

    public bool HasOwnership { get; }

    public static SingleInstanceGuard Acquire(string name = ApplicationLockName)
    {
        string lockPath = GetLockPath(name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            var lockFile = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new SingleInstanceGuard(lockFile, hasOwnership: true);
        }
        catch (IOException)
        {
            return new SingleInstanceGuard(lockFile: null, hasOwnership: false);
        }
    }

    private static string GetLockPath(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string fileName = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        return Path.Combine(Path.GetTempPath(), "PulseRelay", "locks", $"{fileName}.lock");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lockFile?.Dispose();
    }
}
