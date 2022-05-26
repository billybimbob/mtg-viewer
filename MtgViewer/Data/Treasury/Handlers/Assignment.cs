namespace MtgViewer.Data.Treasury.Handlers;

internal readonly record struct Assignment<TSource>(TSource Source, int Copies, Storage Target);
