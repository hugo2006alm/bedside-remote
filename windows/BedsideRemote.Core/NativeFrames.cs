using System.Buffers.Binary;

namespace BedsideRemote.Core;

public static class NativeFrames
{
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size is 0 or > Protocol.MaxBytes) throw new InvalidDataException("Invalid native message length.");
        var body = new byte[size];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return body;
    }
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length is 0 or > Protocol.MaxBytes) throw new InvalidDataException("Invalid native message length.");
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
