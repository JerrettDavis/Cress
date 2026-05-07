namespace Cress.Testing;

public sealed class CressTestFailureException : Exception
{
    public CressTestFailureException(string message)
        : base(message)
    {
    }
}
