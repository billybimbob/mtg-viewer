using System;
using System.IO;

namespace MtgViewer.Tests.Utils;

public sealed class TempFileName : IDisposable
{
    private readonly Lazy<string> _lazy = new(Path.GetTempFileName);

    public string Value => _lazy.Value;

    public void Dispose()
    {
        if (_lazy.IsValueCreated && File.Exists(_lazy.Value))
        {
            File.Delete(_lazy.Value);
        }
    }
}
