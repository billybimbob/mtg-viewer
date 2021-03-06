using Xunit;

using MtgViewer.Services;

namespace MtgViewer.Tests.Services;

public class ParseTextFilterTests
{
    private readonly ParseTextFilter _parseFilter;

    public ParseTextFilterTests(ParseTextFilter parseFilter)
    {
        _parseFilter = parseFilter;
    }

    [Fact]
    public void Parse_Null_DefaultFilter()
    {
        const string? nullString = null;

        var filter = _parseFilter.Parse(nullString);

        Assert.Equal(default, filter);
    }

    [Fact]
    public void Parse_EmptyString_DefaultFilter()
    {
        const string empty = "";

        var filter = _parseFilter.Parse(empty);

        Assert.Equal(default, filter);
    }

    [Fact]
    public void Parse_Whitespace_DefaultFilter()
    {
        const string whitespace = "     ";

        var filter = _parseFilter.Parse(whitespace);

        Assert.Equal(default, filter);
    }

    [Fact]
    public void Parse_TestName_NameFilter()
    {
        const string testName = "test name";

        var filter = _parseFilter.Parse(testName);

        var result = new TextFilter(testName, null, null);

        Assert.Equal(result, filter);
    }

    [Fact]
    public void Parse_TestTypes_TypesFilter()
    {
        const string testTypes = "testType1 testType2";

        var filter = _parseFilter.Parse($"/t {testTypes}");

        var result = new TextFilter(null, testTypes, null);

        Assert.Equal(result, filter);
    }

    [Fact]
    public void Parse_TestText_TextFilter()
    {
        const string testText = "test text for filter";

        var filter = _parseFilter.Parse($"/o {testText}");

        var result = new TextFilter(null, null, testText);

        Assert.Equal(result, filter);
    }

    [Fact]
    public void Parse_TestMixed_MixedFilter()
    {
        const string testName = "test name";
        const string testText = "test text for filter";
        const string testTypes = "testType1 testType2";

        var filter = _parseFilter.Parse($"{testName} /o {testText} /t {testTypes}");

        var result = new TextFilter(testName, testTypes, testText);

        Assert.Equal(result, filter);
    }
}
