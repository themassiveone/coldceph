namespace ColdCeph.Agent.Tests.Support;

internal sealed class EnvScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    private EnvScope(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public static EnvScope Set(string name, string? value) => new(name, value);

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}
