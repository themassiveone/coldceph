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
        if (verb is not ("pages" or "screenshot" or "up" or "down" or "status" or "allow"))
            throw new ArgumentException($"Unknown command '{verb}'. Use pages, screenshot, up, down, status, or allow.");

        if (verb == "pages")
        {
            if (args.Length != 1)
                throw new ArgumentException("pages takes no arguments.");
            return new DebugCommandDto(verb, null);
        }

        if (verb is "down" or "status")
        {
            if (args.Length != 1)
                throw new ArgumentException($"{verb} takes no arguments.");
            return new DebugCommandDto(verb, null);
        }

        if (verb == "up")
        {
            if (args.Length == 1)
                return new DebugCommandDto(verb, null);
            if (args.Length == 2 && args[1] == "--build")
                return new DebugCommandDto(verb, null, RebuildImages: true);
            throw new ArgumentException("up takes no arguments except --build.");
        }

        if (verb == "allow")
        {
            if (args.Length > 2)
                throw new ArgumentException("allow takes at most one host id.");
            return new DebugCommandDto(verb, null, HostId: args.Length == 2 ? args[1] : null);
        }

        if (args.Length > 2)
            throw new ArgumentException("screenshot takes at most one path.");

        return new DebugCommandDto(verb, args.Length == 2 ? args[1] : null);
    }
}
