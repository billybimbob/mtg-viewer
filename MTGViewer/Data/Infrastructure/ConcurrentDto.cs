using System;
using System.Text.Json.Serialization;

namespace MTGViewer.Data.Infrastructure;

public abstract class ConcurrentDto
{
    [JsonInclude]
    public Guid Stamp { get; set; }

    [JsonInclude]
    public byte[] Version { get; set; } = Array.Empty<byte>();

#pragma warning disable IDE1006

    [JsonInclude]
    public uint xmin { get; set; }

#pragma warning restore IDE1006
}
