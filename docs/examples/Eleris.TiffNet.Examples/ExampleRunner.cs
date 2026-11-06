using System.Reflection;

namespace Eleris.TiffNet.Examples;

/// <summary>Runs every example of the guide, page after page.</summary>
/// <remarks>
/// A page of the guide has a class here, with one public static method per example; its body is copied into the page by
/// tools/DocGen between <c>&lt;!-- example: Class.Method --&gt;</c> and <c>&lt;!-- /example --&gt;</c>. The pages are added
/// with the features they document.
/// </remarks>
public static class ExampleRunner
{
    private static readonly Type[] Pages =
    [
        typeof(GettingStartedExamples), typeof(FilesExamples), typeof(PixelExamples), typeof(MetadataExamples), typeof(WritingExamples),
        typeof(LargeImageExamples), typeof(ThreadExamples), typeof(ReliabilityExamples),
    ];

    /// <summary>The names of all the examples, as <c>Class.Method</c>.</summary>
    public static IEnumerable<string> Names => Pages.SelectMany(t => Methods(t).Select(m => $"{t.Name}.{m.Name}"));

    private static IEnumerable<MethodInfo> Methods(Type page) =>
        page.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(m => m.MetadataToken);

    /// <summary>Runs all the examples in the current directory.</summary>
    /// <param name="starting">Called with the name of each example before it runs.</param>
    public static void RunAll(Action<string>? starting = null)
    {
        foreach (var page in Pages)
        {
            foreach (var method in Methods(page))
            {
                starting?.Invoke($"{page.Name}.{method.Name}");
                try
                {
                    if (method.Invoke(null, null) is Task task)
                    {
                        task.GetAwaiter().GetResult();
                    }
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    throw new InvalidOperationException($"{page.Name}.{method.Name} failed: {e.InnerException.Message}", e.InnerException);
                }
            }
        }
    }
}
