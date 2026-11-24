using System.IO.MemoryMappedFiles;
using Xunit;

namespace Ecoar.Tests;

public class RingWriterTests
{
    private const int RingBytes = 262208;

    private static MemoryMappedViewAccessor NewRing(uint magic = 0x524F4345, uint rate = 48000, long writeIndex = 0)
    {
        var mmf = MemoryMappedFile.CreateNew(null, RingBytes);
        var view = mmf.CreateViewAccessor(0, RingBytes);
        view.Write(0, magic);
        view.Write(4, 1u);
        view.Write(8, rate);
        view.Write(12, 1u);
        view.Write(16, 65536u);
        view.Write(32, writeIndex);
        return view;
    }

    [Fact]
    public void Writer_reads_rate_from_header()
    {
        using var w = RingWriter.FromView(NewRing(rate: 44100))!;
        Assert.Equal(44100u, w.SampleRate);
    }

    [Fact]
    public void Write_advances_index_and_stores_samples()
    {
        var view = NewRing();
        using var w = RingWriter.FromView(view)!;
        w.Write(new float[] { 1, 2, 3 });
        Assert.Equal(3L, view.ReadInt64(32));
        Assert.Equal(1f, view.ReadSingle(64));
        Assert.Equal(2f, view.ReadSingle(68));
        Assert.Equal(3f, view.ReadSingle(72));
    }

    [Fact]
    public void Write_wraps_at_capacity()
    {
        var view = NewRing(writeIndex: 65535);
        using var w = RingWriter.FromView(view)!;
        w.Write(new float[] { 7, 8 });
        Assert.Equal(7f, view.ReadSingle(64 + 4 * 65535));
        Assert.Equal(8f, view.ReadSingle(64));
        Assert.Equal(65537L, view.ReadInt64(32));
    }

    [Fact]
    public void TryOpen_rejects_wrong_magic()
    {
        Assert.Null(RingWriter.FromView(NewRing(magic: 0xDEADBEEF)));
    }

    [Fact]
    public void Heartbeat_reads_header_field()
    {
        var view = NewRing();
        view.Write(24, 1234L);
        using var w = RingWriter.FromView(view)!;
        Assert.Equal(1234L, w.Heartbeat);
    }
}
