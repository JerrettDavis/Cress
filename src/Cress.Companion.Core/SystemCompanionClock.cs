namespace Cress.Companion;

public sealed class SystemCompanionClock : ICompanionClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
