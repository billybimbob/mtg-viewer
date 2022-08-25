using System.Linq.Expressions;

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

        var result = _parseFilter.Parse(nullString);

        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_EmptyString_DefaultFilter()
    {
        const string empty = "";

        var result = _parseFilter.Parse(empty);

        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_Whitespace_DefaultFilter()
    {
        const string whitespace = "     ";

        var result = _parseFilter.Parse(whitespace);

        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_GreaterThanSixMana_ManaFilter()
    {
        const string testMana = "> 6";

        var manaFilter = new ManaFilter(ExpressionType.GreaterThan, 6);

        var filter = new TextFilter(null, manaFilter, null, null);

        var result = _parseFilter.Parse($"/c {testMana}");

        Assert.Equal(filter, result);
    }

    [Fact]
    public void Parse_EqualTwoMana_ManaFilter()
    {
        const string testMana = "= 2";

        var manaFilter = new ManaFilter(ExpressionType.Equal, 2);

        var filter = new TextFilter(null, manaFilter, null, null);

        var result = _parseFilter.Parse($"/c {testMana}");

        Assert.Equal(filter, result);
    }

    [Fact]
    public void Parse_LessThanOrEqualThreeMana_ManaFilter()
    {
        const string testMana = "<= 3";

        var manaFilter = new ManaFilter(ExpressionType.LessThanOrEqual, 3);

        var filter = new TextFilter(null, manaFilter, null, null);

        var result = _parseFilter.Parse($"/c {testMana}");

        Assert.Equal(filter, result);
    }

    [Fact]
    public void Parse_EmptyMana_DefaultFilter()
    {
        const string invalidMana = "> invalidValue";

        var result = _parseFilter.Parse($"/c {invalidMana}");

        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_TestName_NameFilter()
    {
        const string testName = "test name";

        var filter = new TextFilter(testName, null, null, null);

        var result = _parseFilter.Parse(testName);

        Assert.Equal(filter, result);
    }

    [Fact]
    public void Parse_TestTypes_TypesFilter()
    {
        const string testTypes = "testType1 testType2";

        var filter = _parseFilter.Parse($"/t {testTypes}");

        var result = new TextFilter(null, null, testTypes, null);

        Assert.Equal(result, filter);
    }

    [Fact]
    public void Parse_TestText_TextFilter()
    {
        const string testText = "test text for filter";

        var filter = _parseFilter.Parse($"/o {testText}");

        var result = new TextFilter(null, null, null, testText);

        Assert.Equal(result, filter);
    }

    [Fact]
    public void Parse_TestMixed_MixedFilter()
    {
        const string testName = "test name";
        const string testText = "test text for filter";
        const string testTypes = "testType1 testType2";

        var filter = _parseFilter.Parse($"{testName} /o {testText} /t {testTypes}");

        var result = new TextFilter(testName, null, testTypes, testText);

        Assert.Equal(result, filter);
    }
}
