using System;

namespace MtgViewer.Tests.Utils;

public sealed class InMemoryConnectionStrings
{
    public string Sqlite { get; } = "Filename=:memory:";

    public string InMemory { get; } = "Test-Database-" + Guid.NewGuid();
}
