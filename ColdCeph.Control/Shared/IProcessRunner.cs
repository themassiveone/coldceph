namespace ColdCeph.Control.Shared;

public interface IProcessRunner
{
    string Run(string fileName, IReadOnlyList<string> arguments);
}
