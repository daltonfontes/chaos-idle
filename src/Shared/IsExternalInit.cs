// Polyfill so C# 9 records/init setters compile on netstandard2.1.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
