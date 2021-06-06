using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using MTGViewer.Models;
using MTGViewer.Services;

namespace MTGViewer.Pages.Cards
{
    public class CreateModel : PageModel
    {
        private const string QUERY = "match";
        private readonly MTGCardContext _context;
        private readonly MTGFetchService _fetch;

        public CreateModel(MTGCardContext context, MTGFetchService fetch)
        {
            _context = context;
            _fetch = fetch;
        }

        public IActionResult OnGet()
        {
            Console.WriteLine("on get");
            return Page();
        }

        [BindProperty]
        public Card Card { get; set; }

        [BindProperty]
        public string Picked { get; set; }
        public IEnumerable<Card> Matches { get; private set; }


        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {
            Console.WriteLine("on post");

            if (string.IsNullOrEmpty(Picked))
            {
                if (ModelState.IsValid)
                {
                    Matches = await _fetch.MatchAsync(Card);
                }

                return Page();
            }

            // TempData[QUERY] = JsonConvert.SerializeObject(Card);

            Console.WriteLine($"picked {Picked}");
            bool any = await _context.Cards.Where(c => c.Id == Picked).AnyAsync();
            if (!any)
            {
                Card = await _fetch.GetIdAsync(Picked);

                _context.Cards.Add(Card);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }

    }
}
