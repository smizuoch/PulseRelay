using System.Globalization;

namespace PulseRelay.Tests;

/// <summary>
/// Pins <see cref="CultureInfo.CurrentUICulture"/> for the scope of a test so copy
/// assertions don't depend on the host machine's UI language. xUnit may reuse threads,
/// so always restore.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous;

    public CultureScope(string cultureName)
    {
        _previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
    }

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
