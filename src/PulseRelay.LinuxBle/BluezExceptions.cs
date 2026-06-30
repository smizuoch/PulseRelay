namespace PulseRelay.LinuxBle;

public sealed class BluezDbusCallException : Exception
{
    public BluezDbusCallException(string errorName, string message)
        : base(message) =>
        ErrorName = errorName;

    public string ErrorName { get; }
}

public sealed class BluezPairingRequiredException : Exception
{
    public BluezPairingRequiredException(string message)
        : base(message)
    {
    }

    public BluezPairingRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
