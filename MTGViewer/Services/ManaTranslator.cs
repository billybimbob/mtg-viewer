using System;
using System.Linq;

#nullable enable

namespace MTGViewer.Services
{
    public class ManaTranslator : ISymbolTranslator
    {
        public string TranslateMana(string mana)
        {
            var cost = mana.Replace("/", "").ToLower();

            cost = cost switch
            {
                "t" => "tap",
                _ => cost
            };

            return $"<i class=\"mr-2 ms ms-{cost} ms-cost\"></i>";
        }


        public string TranslateLoyalty(string? direction, string loyalty)
        {
            direction = direction switch
            {
                _ when direction is null => "zero",
                "+" => "up",
                "−" => "down",
                _ => throw new ArgumentException("Unexpected loyalty group")
            };

            return $"<i class=\"m-1 ms ms-loyalty-{direction} ms-loyalty-{loyalty}\"></i>";
        }


        public string TranslateSaga(string saga, bool isFinal)
        {
            var sagaInt = ParseRomanNumeral(saga);

            return $"<i class=\"ms ms-saga ms-saga-{sagaInt}\"></i>";
        }


        private int ParseRomanNumeral(string romanNumeral)
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


        private int RomanLetterValue(char romanLetter) => romanLetter switch
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
}