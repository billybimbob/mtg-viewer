using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MTGViewer.Tests.Utils;


[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TestPriorityAttribute : Attribute
{
    public int Priority { get; init; }

    public TestPriorityAttribute(int priority) => Priority = priority;
}


public class PriorityOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(
        IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        string assemblyName = typeof(TestPriorityAttribute).AssemblyQualifiedName!;
        var sortedMethods = new SortedDictionary<int, List<TTestCase>>();

        foreach (var testCase in testCases)
        {
            int priority = testCase.TestMethod.Method
                .GetCustomAttributes(assemblyName)
                .FirstOrDefault()
                ?.GetNamedArgument<int>(nameof(TestPriorityAttribute.Priority)) ?? 0;

            GetOrCreate(sortedMethods, priority)
                .Add(testCase);
        }

        var sorted = sortedMethods.Keys
            .SelectMany(priority => sortedMethods[priority]
                .OrderBy(testCase => testCase.TestMethod.Method.Name));

        foreach (TTestCase testCase in sorted)
        {
            yield return testCase;
        }
    }


    private static TValue GetOrCreate<TKey, TValue>(
        IDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : struct
        where TValue : new()
    {
        return dictionary.TryGetValue(key, out var result)
            ? result
            : (dictionary[key] = new());
    }
}