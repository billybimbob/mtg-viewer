using System;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Services;

public readonly struct Percent
{
    private readonly int _value;

    public Percent(int value)
    {
        int floor = Math.Max(0, value);
        int ceil = Math.Min(floor, 100);

        _value = ceil;
    }

    public static implicit operator int(Percent percent)
    {
        return percent._value;
    }

    public static implicit operator Percent(int value)
    {
        return new Percent(value);
    }

    public static Percent operator +(Percent a, Percent b)
    {
        return new Percent(a._value + b._value);
    }

    public override string ToString()
    {
        return $"{_value}%";
    }
}

public class LoadingProgress
{
    private readonly ILogger<LoadingProgress> _logger;
    private int _ticks;
    private int _defaultProgress;

    public LoadingProgress(ILogger<LoadingProgress> logger)
    {
        _logger = logger;
    }

    public event Action<Percent>? ProgressUpdate;

    public Percent Current { get; private set; }

    public int Ticks
    {
        get => _ticks;
        set
        {
            if (value <= 0)
            {
                return;
            }

            // end is 100, issue if ops after this
            _ticks = value;
            _defaultProgress = (100 - Current) / _ticks;
        }
    }

    public void AddProgress(Percent progress)
    {
        if (progress == 0)
        {
            return;
        }

        Current += progress;
        ProgressUpdate?.Invoke(Current);

        _logger.LogInformation("Percent updated to {Current}", Current);
    }

    public void AddProgress()
    {
        AddProgress(_defaultProgress);
    }

    public void Reset()
    {
        Current = 0;
        ProgressUpdate?.Invoke(Current);

        _logger.LogInformation($"Progress reset");
    }
}
