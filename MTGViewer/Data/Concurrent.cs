using System;
using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Data;

// each internal property is ignored by convention
public abstract class Concurrent
{
    [ConcurrencyCheck]
    internal Guid Stamp { get; set; }

    [Timestamp]
    internal byte[] Version { get; set; } = Array.Empty<byte>();

    internal uint xmin { get; set; }
}
