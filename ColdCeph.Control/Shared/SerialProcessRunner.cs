namespace ColdCeph.Control.Shared;

public sealed class SerialProcessRunner : IProcessRunner
{
    private readonly IProcessRunner _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SerialProcessRunner(IProcessRunner inner)
    {
        _inner = inner;
    }

    public string Run(string fileName, IReadOnlyList<string> arguments)
    {
        _gate.Wait();
        try
        {
            return _inner.Run(fileName, arguments);
        }
        finally
        {
            _gate.Release();
        }
    }
}
