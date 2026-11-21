namespace Pumpkin.DotNet.Runtime.Interop;

public abstract class WitResource<THandle> : IDisposable
    where THandle : class, IDisposable
{
    private THandle? _handle;
    private readonly bool _ownsHandle;
    private int _disposed;

    protected WitResource(THandle handle, bool ownsHandle = true)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    protected THandle Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _handle!;
        }
    }

    /// <summary>
    /// Internal access to the underlying WIT handle for interop calls within the runtime assembly.
    /// </summary>
    internal THandle WitHandle => Handle;

    internal THandle DetachHandle()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            throw new ObjectDisposedException(GetType().FullName);

        var handle = _handle!;
        _handle = null;
        GC.SuppressFinalize(this);
        return handle;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_ownsHandle)
        {
            _handle?.Dispose();
        }

        _handle = null;
    }

    ~WitResource()
    {
        Dispose(disposing: false);
    }
}