using System;
using System.Linq;

namespace MTGViewer.Services;


public class ManaTranslator : ISymbolTranslator
{
    public string ManaString(ManaSymbol symbol)
    {
        var cost = symbol.Value.Replace("/", "").ToLower();

        cost = cost switch
        {
            "t" => "tap",
            _ => cost
        };

        return $@"<i class=""m-1 ml-0 ms ms-{cost} ms-cost""></i>";
    }


    public string LoyaltyString(LoyaltySymbol symbol)
    {
        var (_, direction, loyalty) = symbol;

        direction = direction switch
        {
            _ when direction is null => "zero",
            "+" => "up",
            "−" => "down",
            _ => throw new ArgumentException("Unexpected loyalty group")
        };

        return $@"<i class=""m-1 ms ms-loyalty-{direction} ms-loyalty-{loyalty}""></i>";
    }


    public string SagaString(SagaSymbol symbol)
    {
        var saga = ParseRomanNumeral(symbol.Value);

        return $@"<i class=""ml-1 ms ms-saga ms-saga-{saga}""></i>";
    }


    private static int ParseRomanNumeral(string romanNumeral)
    {
        var result = romanNumeral
            .ToUpper()
            .Select(RomanLetterValue)
            .Sum();

        if (romanNumeral.Contains("IV")) // || romanNumeral.Contains("IX"))
        {
            result -= 2;
        }

        // if (romanNumeral.Contains("XL") || romanNumeral.Contains("XC"))
        // {
        //     result -= 20;
        // }

        // if (romanNumeral.Contains("CD") || romanNumeral.Contains("CM"))
        // {
        //     result -= 200;
        // }

        return result;
    }


    private static int RomanLetterValue(char romanLetter) => romanLetter switch
    {
        'I' => 1,
        'V' => 5,
        // 'X' => 10,
        // 'L' => 50,
        // 'C' => 100,
        // 'D' => 500,
        // 'M' => 1000,
        _ => 0
    };
}