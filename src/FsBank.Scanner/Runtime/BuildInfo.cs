namespace FsBank.Scanner.Runtime;

internal static class BuildInfo
{
#if DEBUG
    public const string Configuration="Debug";
#else
    public const string Configuration="Release";
#endif
}
