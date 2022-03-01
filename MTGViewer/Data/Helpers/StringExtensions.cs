using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Components;
using Npgsql;

namespace MTGViewer.Data;

public static class StringExtensions
{
    public static HtmlString ToHtmlString(this string value) => new(value);

    public static MarkupString ToMarkupString(this string value) => new(value);

    public static string WithHttps(this string url) =>
        new UriBuilder(url) { Scheme = "https", Port = -1 }.ToString();


    public static string ToNpgsqlConnectionString(this string pgUrl)
    {
        const string pgUrlPattern = @"postgres(?:ql)?:\/\/"
            + $@"(?<user>[^:]*):"
            + $@"(?<password>[^@]*)@"
            + $@"(?<host>[^:]*):"
            + $@"(?<port>[^/]*)\/"
            + $@"(?<database>.*)";

        ArgumentNullException.ThrowIfNull(pgUrl);

        var urlValues = Regex.Match(pgUrl, pgUrlPattern).Groups;

        var conn = new NpgsqlConnectionStringBuilder
        {
            Username = urlValues["user"].Value,
            Password = urlValues["password"].Value,
            Host = urlValues["host"].Value,
            Port = int.Parse( urlValues["port"].Value ),
            Database = urlValues["database"].Value,
            SslMode = SslMode.Prefer
        };

        return conn.ConnectionString;
    }
}