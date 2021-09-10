using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Services;
using MTGViewer.Data;


namespace MTGViewer.Pages.Boxes
{
    public class IndexModel : PageModel
    {
        private readonly ISharedStorage _sharedStorage;

        public IndexModel(ISharedStorage sharedStorage)
        {
            _sharedStorage = sharedStorage;
        }


        [TempData]
        public string PostMessage { get; set; }

        public IReadOnlyList<Box> Boxes { get; private set; }


        public async Task OnGetAsync()
        {
            Boxes = await _sharedStorage.Shares
                .Include(b => b.Bin)
                .Include(b => b.Cards
                    .Where(ca => ca.Amount > 0)
                    .OrderBy(ca => ca.Card.Name))
                    .ThenInclude(ca => ca.Card)
                .OrderBy(b => b.Id)
                .ToListAsync();
        }
    }
}