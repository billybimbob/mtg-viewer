using System;

namespace MTGViewer.Data;

internal abstract class ConcurrentDto
{
    public Guid Stamp { get; set; }

    public byte[] Version { get; set; } = Array.Empty<byte>();

    public uint xmin { get; set; }
}
