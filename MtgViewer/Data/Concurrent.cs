using System;
using System.ComponentModel.DataAnnotations;

namespace MtgViewer.Data;

// each internal property is ignored by convention
public abstract class Concurrent
{
    [ConcurrencyCheck]
    internal Guid Stamp { get; set; }

    [Timestamp]
    internal byte[] Version { get; set; } = Array.Empty<byte>();

#pragma warning disable IDE1006

    internal uint xmin { get; set; }

#pragma warning restore IDE1006

}
