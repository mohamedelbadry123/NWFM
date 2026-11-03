namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Runs control-plane work out of band on this node, one item at a time per worker, each in its own
/// DI scope inside the platform scope (never bound to a tenant). Items are not durable: whatever they
/// do must be resumable from the catalog (provisioning checkpoints, migration state), and the sweep
/// re-queues work a stopped node left behind.
/// </summary>
public interface IPlatformWorkQueue
{
    /// <returns>False when the queue is full or the host is stopping.</returns>
    bool Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> work);
}
