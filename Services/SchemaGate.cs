namespace RadioV2.Services;

/// <summary>
/// Orders stations.db reads behind its schema migration.
///
/// Schema init moved off the startup path so the window could appear immediately,
/// which means a page can now reach <see cref="StationService"/> before
/// <c>DatabaseInitService</c> has run. On the shipped seed that is harmless, but a
/// user upgrading from the legacy single-database layout has no <c>IsFeatured</c>
/// column until the migration adds it, and EF selects that column on every station
/// query — so the first page they open would fail outright.
///
/// This is an ordering primitive only. It is marked ready whether init succeeded or
/// failed: a failure is logged and surfaced by the normal error path, and refusing to
/// ever complete would turn one bad migration step into a permanently hung app.
/// </summary>
public sealed class SchemaGate
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once schema init has finished, successfully or not.</summary>
    public Task Ready => _ready.Task;

    public bool IsReady => _ready.Task.IsCompleted;

    public void MarkReady() => _ready.TrySetResult();
}
