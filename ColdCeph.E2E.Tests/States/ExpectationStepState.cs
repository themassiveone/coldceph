using Xcepto.States;

namespace ColdCeph.E2E.Tests.States;

internal sealed class ExpectationStepState : XceptoState
{
    private readonly Func<Task<bool>> _predicate;

    public ExpectationStepState(string name, Func<Task<bool>> predicate) : base(name)
    {
        _predicate = predicate;
    }

    public override Task OnEnter(IServiceProvider serviceProvider) => Task.CompletedTask;

    public override async Task<bool> EvaluateConditionsForTransition(IServiceProvider serviceProvider)
    {
        try
        {
            return await _predicate();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
