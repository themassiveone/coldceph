using ColdCeph.Debug.Features.Host.Interfaces;
using ColdCeph.Debug.Features.Host.Services;

namespace ColdCeph.Debug.Features.Host.Controllers;

public sealed class HostController
{
    private readonly IDebugArgParser _parser;
    private readonly HostCommandService _commands;

    public HostController(IDebugArgParser parser, HostCommandService commands)
    {
        _parser = parser;
        _commands = commands;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            return await _commands.ExecuteAsync(_parser.Parse(args), cancellationToken);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.Message);
            return 1;
        }
    }
}
