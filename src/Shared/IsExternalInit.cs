// Polyfill so C# 9 records/init setters compile on netstandard2.1 (and in Unity).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
