using Layout.Common;
using Layout.CSharp;
using Layout.Rust;

namespace LayoutBench;

public static class Engines
{
    public static IParagraphLayouter Create(string engine, FontSet fonts) => engine switch
    {
        "csharp" => new HarfBuzzParagraphLayouter(fonts),
        "rust" => new RustParagraphLayouter(fonts),
        _ => throw new NotSupportedException($"engine '{engine}' is not available in this build"),
    };
}
