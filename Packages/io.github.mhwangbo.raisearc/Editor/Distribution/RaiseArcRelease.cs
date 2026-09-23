namespace RaiseArc.Editor
{
    /// <summary>Current release entrypoints. Legacy implementation identities remain supported.</summary>
    public static class RaiseArcRelease
    {
        public static void ExportAndExit() => PrincessStudio.Editor.PackageRelease.ExportAndExit();
        public static void BuildSampleAndExit() => PrincessStudio.Editor.PackageRelease.BuildSampleAndExit();
    }
}
