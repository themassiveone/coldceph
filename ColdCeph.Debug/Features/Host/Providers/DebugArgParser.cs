using ColdCeph.Debug.Features.Host.DTOs;
using ColdCeph.Debug.Features.Host.Interfaces;

namespace ColdCeph.Debug.Features.Host.Providers;

public sealed class DebugArgParser : IDebugArgParser
{
    public DebugCommandDto Parse(string[] args)
    {
        if (args.Length == 0)
            return new DebugCommandDto("screenshot", null);

        var verb = args[0];
        if (verb is not ("pages" or "screenshot"))
            throw new ArgumentException($"Unknown command '{verb}'. Use pages or screenshot.");

        if (verb == "pages")
        {
            if (args.Length != 1)
                throw new ArgumentException("pages takes no arguments.");
            return new DebugCommandDto(verb, null);
        }

        if (args.Length > 2)
            throw new ArgumentException("screenshot takes at most one path.");

        return new DebugCommandDto(verb, args.Length == 2 ? args[1] : null);
    }
}
