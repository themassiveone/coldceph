namespace ColdCeph.Agent.Shared;

public interface IProcessRunner
{
    string Run(string fileName, IReadOnlyList<string> arguments);
}
