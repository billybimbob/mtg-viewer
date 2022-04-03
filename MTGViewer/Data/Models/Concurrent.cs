using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MTGViewer.Data.Concurrency;


// each internal property is ignored by convention
public abstract class Concurrent
{
    [ConcurrencyCheck]
    internal Guid Stamp { get; set; }

    [Timestamp]
    internal byte[] Version { get; set; } = Array.Empty<byte>();

    internal uint xmin { get; set; }
}


internal abstract class ConcurrentDto
{
    [JsonInclude]
    public Guid Stamp { get; set; }

    [JsonInclude]
    public byte[] Version { get; set; } = Array.Empty<byte>();

    [JsonInclude]
    public uint xmin { get; set; }
}
