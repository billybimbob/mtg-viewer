using System;
using System.Collections;
using System.Collections.Generic;

namespace MTGViewer.Data;

public readonly record struct Seek<T>(T? Before, T? After) where T : class;


// public class SeekList<T> : IReadOnlyList<T>
// {
// }