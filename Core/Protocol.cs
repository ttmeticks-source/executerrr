using System.Buffers.Binary;

namespace Bozo.Core;

public enum Op : byte
{
    Execute     = 0x01,
    Output      = 0x02,
    Ready       = 0x03,
    Ping        = 0x04,
    SetAutoExec = 0x05,
    Clear       = 0x06,
}

public readonly record struct Frame(Op Op, byte[] Payload)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Payload);

    public static Frame Of(Op op, string text) =>
        new(op, System.Text.Encoding.UTF8.GetBytes(text));

    public static Frame Of(Op op) => new(op, Array.Empty<byte>());

    public byte[] Serialize()
    {
        var buf = new byte[5 + Payload.Length];
        buf[0] = (byte)Op;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(1, 4), Payload.Length);
        Payload.CopyTo(buf, 5);
        return buf;
    }

    public static bool TryRead(Stream s, out Frame frame)
    {
        frame = default;
        var head = new byte[5];
        if (!ReadExactly(s, head)) return false;
        var len = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(1, 4));
        if (len is < 0 or > 16 * 1024 * 1024) return false;
        var payload = new byte[len];
        if (len > 0 && !ReadExactly(s, payload)) return false;
        frame = new Frame((Op)head[0], payload);
        return true;
    }

    private static bool ReadExactly(Stream s, byte[] buf)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }
}
