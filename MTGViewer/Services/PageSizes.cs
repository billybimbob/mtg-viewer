using System.Linq;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;


namespace MTGViewer.Services
{
    public class PageSizes
    {
        private static readonly string[] PagesNamespace = new[] 
        {
            nameof(MTGViewer), nameof(MTGViewer.Pages)
        };


        private readonly IConfigurationSection _config;
        private readonly int _default;

        public PageSizes(IConfiguration config)
        {
            _config = config.GetSection("PageSizes");
            _default = _config.GetValue("Default", 10);

            Limit = _config.GetValue("Limit", 256);
        }


        public int Limit { get; }

        public int GetSize(PageModel page)
        {
            var route = page.GetType().FullName
                .Split('.')
                .Except(PagesNamespace)
                .ToArray();

            var sectionKey = string.Join(':', route.SkipLast(1));
            var pageName = route.Last().Replace("Model", "");

            return _config
                .GetSection(sectionKey)
                .GetValue(pageName, _default);
            }
    }
}