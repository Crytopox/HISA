namespace Hisa.Core;

/// <summary>Await cleanup while the UI dispatcher is alive, without indefinitely preventing shutdown.</summary>
public static class BoundedCleanup
{
    public static async Task<bool> RunAsync(Func<Task> cleanup, TimeSpan timeout)
    {
        try { await cleanup().WaitAsync(timeout); return true; }
        catch (Exception) { return false; } // Shutdown continues; repository readers have their own cancellation/deadline.
    }
}
