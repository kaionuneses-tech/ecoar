using System.IO.MemoryMappedFiles;

namespace Ecoar;

/// <summary>
/// Single writer of the shared ring read by EcoarApo.dll inside audiodg.
/// Layout mirrors apo/RingLayout.h (64-byte header + 65536 mono float samples).
/// </summary>
public sealed unsafe class RingWriter : IRingSink, IDisposable
{
    public const uint Magic = 0x524F4345;   // "ECOR"
    public const int Capacity = 65536;
    public const int HeaderBytes = 64;
    public const int RingBytes = HeaderBytes + Capacity * sizeof(float);
    public const string GlobalName = @"Global\EcoarRing-v1";
    public const string FilePath = @"C:\ProgramData\Ecoar\ring.bin";

    private const int OffRate = 8, OffHeartbeat = 24, OffWriteIndex = 32;

    private readonly MemoryMappedViewAccessor _view;
    private readonly IDisposable? _owner;
    private byte* _base;

    private RingWriter(MemoryMappedViewAccessor view, IDisposable? owner)
    {
        _view = view;
        _owner = owner;
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _base = p + _view.PointerOffset;
    }

    /// <summary>Opens the ring created by the APO; null when no APO is running or the header is foreign.</summary>
    public static RingWriter? TryOpen()
    {
        try
        {
            var mmf = MemoryMappedFile.OpenExisting(GlobalName, MemoryMappedFileRights.ReadWrite);
            return FromView(mmf.CreateViewAccessor(0, RingBytes), mmf);
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException) { }

        try
        {
            if (!File.Exists(FilePath)) return null;
            var mmf = MemoryMappedFile.CreateFromFile(
                new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite),
                null, RingBytes, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            return FromView(mmf.CreateViewAccessor(0, RingBytes), mmf);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static RingWriter? FromView(MemoryMappedViewAccessor view, IDisposable? owner = null)
    {
        if (view.ReadUInt32(0) != Magic)
        {
            view.Dispose();
            owner?.Dispose();
            return null;
        }
        return new RingWriter(view, owner);
    }

    public uint SampleRate => Volatile.Read(ref *(uint*)(_base + OffRate));

    public long Heartbeat => Volatile.Read(ref *(long*)(_base + OffHeartbeat));

    public void Write(ReadOnlySpan<float> mono)
    {
        long index = Volatile.Read(ref *(long*)(_base + OffWriteIndex));
        float* data = (float*)(_base + HeaderBytes);
        for (int i = 0; i < mono.Length; i++)
            data[(index + i) & (Capacity - 1)] = mono[i];
        // Release store: readers that see the new index also see the samples.
        Volatile.Write(ref *(long*)(_base + OffWriteIndex), index + mono.Length);
    }

    public void Dispose()
    {
        if (_base != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _base = null;
        }
        _view.Dispose();
        _owner?.Dispose();
    }
}
