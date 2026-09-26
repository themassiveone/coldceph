using Xcepto.States;

namespace ColdCeph.E2E.Tests.States;

internal sealed class ActionStepState : XceptoState
{
    private readonly Func<Task> _action;

    public ActionStepState(string name, Func<Task> action) : base(name)
    {
        _action = action;
    }

    public override Task OnEnter(IServiceProvider serviceProvider) => _action();

    public override Task<bool> EvaluateConditionsForTransition(IServiceProvider serviceProvider)
        => Task.FromResult(true);
}
