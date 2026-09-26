namespace ColdCeph.Node.Shared;

public interface IProcessRunner
{
    string Run(string fileName, IReadOnlyList<string> arguments);
}
