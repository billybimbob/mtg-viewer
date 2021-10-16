#nullable enable

namespace MTGViewer.Services
{
    public interface ISymbolTranslator
    {
        string TranslateMana(string mana);

        string TranslateLoyalty(string? direction, string loyalty);

        string TranslateSaga(string saga, bool isFinal);
    }
}