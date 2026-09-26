namespace ColdCeph.Control.Shared;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
