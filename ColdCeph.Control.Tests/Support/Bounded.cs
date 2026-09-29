namespace ColdCeph.Control.Tests.Support;

/// <summary>
/// Runs work with a wall-clock bound so a deadlock fails the test instead of hanging the run.
/// The regressions this guards — an unbounded process wait, and a pipe-read order that deadlocks
/// on a chatty child — both present as "never returns", which is the one failure a plain
/// assertion cannot report.
/// </summary>
public static class Bounded
{
    public static T Within<T>(TimeSpan limit, Func<T> work, string what)
    {
        // Deliberately not cancellable: the point is to notice that it did not come back.
        var task = Task.Run(work);
        if (!task.Wait(limit))
            throw new TimeoutException($"{what} did not finish within {limit.TotalSeconds:0.#}s; it is deadlocked.");
        return task.Result;
    }

    public static void Within(TimeSpan limit, Action work, string what)
        => Within(limit, () => { work(); return true; }, what);
}
