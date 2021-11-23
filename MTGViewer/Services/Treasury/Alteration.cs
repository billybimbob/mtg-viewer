using System;
using MTGViewer.Data;

namespace MTGViewer.Services;


/// <summary Possible change to the Treasury. </summary>
public abstract record Alteration
{
    protected static T NonNullOrThrow<T>(T? value, string argName) where T : class
    {
        return value ?? throw new ArgumentNullException(argName);
    }

    protected static int PositiveOrThrow(int value, string argName)
    {
        return value > 0 ? value : throw new ArgumentException(argName);
    }
}


/// <summary>
/// <see cref="Alteration"/> indicating that the specified <see cref="Amount"/> 
/// can be removed by the <see cref="Withdrawl.NumCopies"/> amount
/// </summary>
public record Withdrawl(int AmountId, int NumCopies) : Alteration
{
    private int _numCopies = PositiveOrThrow(NumCopies, nameof(NumCopies));

    public int NumCopies
    {
        get => _numCopies;
        init => _numCopies = PositiveOrThrow(value, nameof(NumCopies));
    }
}


/// <summary>  
/// <see cref="Alteration"/> indicating addition changes to the Treasury
/// </summary>
public abstract record Deposit : Alteration;


/// <summary>
/// <see cref="Deposit"/> indicating that an existing <see cref="Amount"/> 
/// can be added with the <see cref="Addition.NumCopies"/> amount
/// </summary>
public record Addition(int AmountId, int NumCopies) : Deposit
{
    private int _numCopies = PositiveOrThrow(NumCopies, nameof(NumCopies));

    public int NumCopies
    {
        get => _numCopies;
        init => _numCopies = PositiveOrThrow(value, nameof(NumCopies));
    }
}


/// <summary>
/// <see cref="Deposit"/> indicating that a new <see cref="Amount"/> 
/// can be added with the <see cref="Extension.NumCopies"/> amount
/// </summary>
public record Extension(string CardId, int BoxId, int NumCopies) : Deposit
{
    private string _cardId = NonNullOrThrow(CardId, nameof(CardId));
    private int _numCopies = PositiveOrThrow(NumCopies, nameof(NumCopies));

    public string CardId 
    {
        get => _cardId;
        init => _cardId = NonNullOrThrow(value, nameof(CardId));
    }

    public int NumCopies
    {
        get => _numCopies;
        init => _numCopies = PositiveOrThrow(value, nameof(NumCopies));
    }
}