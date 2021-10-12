using System.Collections.Generic;

namespace MTGViewer.Services
{
    public interface IMTGSymbols
    {
        string[] FindSymbols(string mtgText);

        string JoinSymbols(IEnumerable<string> mtgSymbols);

        string InjectIcons(string mtgText);
    }
}