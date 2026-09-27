using ColdCeph.Debug.Features.Compose.Interfaces;

namespace ColdCeph.Debug.Features.Compose.Services;

public sealed class ComposeStackService
{
    private readonly IComposeCli _compose;
    private readonly IControlHealth _health;
    private readonly IControlProcess _control;

    public ComposeStackService(IComposeCli compose, IControlHealth health, IControlProcess control)
    {
        _compose = compose;
        _health = health;
        _control = control;
    }

    public void Up(bool rebuildImages)
    {
        Console.Write(_compose.Run(rebuildImages
            ? ["up", "-d", "--build", "--wait"]
            : ["up", "-d", "--wait"]));
        if (!_health.IsReady())
            _control.Start();
    }

    public void Down()
    {
        Console.Write(_compose.Run("down"));
        if (_control.Owned)
            _control.Stop();
    }

    public string Status()
    {
        var compose = _compose.Run("ps", "-a").TrimEnd();
        var control = _health.IsReady() ? "control: ready" : "control: not ready";
        if (string.IsNullOrEmpty(compose))
            return control;
        return compose + Environment.NewLine + control;
    }
}
