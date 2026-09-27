using ColdCeph.Debug.Features.Compose.Services;

namespace ColdCeph.Debug.Features.Compose.Controllers;

public sealed class ComposeController
{
    private readonly ComposeStackService _stack;

    public ComposeController(ComposeStackService stack)
    {
        _stack = stack;
    }

    public int Up(bool rebuildImages)
    {
        _stack.Up(rebuildImages);
        Console.WriteLine(rebuildImages ? "compose: up --build" : "compose: up");
        return 0;
    }

    public int Down()
    {
        _stack.Down();
        Console.WriteLine("compose: down");
        return 0;
    }

    public int Status()
    {
        Console.WriteLine(_stack.Status());
        return 0;
    }
}
