namespace ToyPadMaui.Services;

// Bridge for files opened/shared into the app from the OS (e.g. iOS "Open
// with / Share to LEGOPATCHER"). The main page subscribes to ZipReceived.
public static class IncomingFile
{
    // Raised with the raw bytes of an incoming .zip. Marshalled to main thread
    // by the subscriber as needed.
    public static event Func<byte[], Task>? ZipReceived;

    // If a file arrives before the UI is ready, it's stashed here.
    static byte[]? _pending;
    static readonly object _lock = new();

    public static async Task HandleZipBytesAsync(byte[] bytes)
    {
        Func<byte[], Task>? handler;
        lock (_lock) handler = ZipReceived;

        if (handler is null)
        {
            lock (_lock) _pending = bytes;     // deliver once UI subscribes
            return;
        }
        await handler(bytes);
    }

    // Called by the page once it's ready to flush any file that arrived early.
    public static async Task FlushPendingAsync()
    {
        byte[]? pending;
        lock (_lock) { pending = _pending; _pending = null; }
        if (pending is null) return;

        Func<byte[], Task>? handler;
        lock (_lock) handler = ZipReceived;
        if (handler is not null) await handler(pending);
    }
}
