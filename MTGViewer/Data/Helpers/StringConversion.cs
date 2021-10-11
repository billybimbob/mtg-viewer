using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Components;

namespace MTGViewer.Data
{
    public static class StringConversionExtension
    {
        public static HtmlString ToHtmlString(this string value) => new(value);

        public static MarkupString ToMarkupString(this string value) => new(value);
    }
}