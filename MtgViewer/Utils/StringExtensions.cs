using System;
using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Html;

using Npgsql;

namespace MtgViewer.Utils;

internal static partial class StringExtensions
{
    public static HtmlString ToHtmlString(this string value) => new(value);

    public static MarkupString ToMarkupString(this string value) => new(value);

    public static string WithHttps(this string url) =>
        new UriBuilder(url) { Scheme = "https", Port = -1 }.ToString();

    public static string ToNpgsqlConnectionString(this string pgUrl)
    {
        ArgumentNullException.ThrowIfNull(pgUrl);

        var urlValues = PostgresUrl().Match(pgUrl).Groups;

        var invariant = CultureInfo.InvariantCulture;

        var conn = new NpgsqlConnectionStringBuilder
        {
            Username = urlValues["user"].Value,
            Password = urlValues["password"].Value,
            Host = urlValues["host"].Value,
            Port = int.Parse(urlValues["port"].Value, invariant),
            Database = urlValues["database"].Value,
            SslMode = SslMode.Prefer
        };

        return conn.ConnectionString;
    }

    private const string pgUrlPattern = @"postgres(?:ql)?:\/\/"
        + $@"(?<user>[^:]*):"
        + $@"(?<password>[^@]*)@"
        + $@"(?<host>[^:]*):"
        + $@"(?<port>[^/]*)\/"
        + $@"(?<database>.*)";

    [GeneratedRegex(pgUrlPattern)]
    private static partial Regex PostgresUrl();
}
