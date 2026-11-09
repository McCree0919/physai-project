using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    // Command line: -executeMethod BuildScript.Build
    public static void Build()
    {
        const string scene = "Assets/Scenes/ReachTraining.unity";
        if (!File.Exists(scene))
        {
            throw new BuildFailedException("Generate the scene with Tools/Build Reach Arena first.");
        }

        BuildTarget target;
        string filename;
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
                target = BuildTarget.StandaloneWindows64;
                filename = "Reach.exe";
                break;
            case RuntimePlatform.OSXEditor:
                target = BuildTarget.StandaloneOSX;
                filename = "Reach.app";
                break;
            case RuntimePlatform.LinuxEditor:
                target = BuildTarget.StandaloneLinux64;
                filename = "Reach.x86_64";
                break;
            default:
                throw new PlatformNotSupportedException("A desktop Unity Editor is required.");
        }

        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            throw new BuildFailedException("Install Unity standalone build support for " + target);
        }
        string output = Path.GetFullPath("Builds/Reach");
        Directory.CreateDirectory(output);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene },
            locationPathName = Path.Combine(output, filename),
            target = target,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
        {
            throw new BuildFailedException($"Reach build failed: {report.summary.result}, {report.summary.totalErrors} errors.");
        }
        Debug.Log($"REACH_BUILD_SUCCEEDED: {Path.Combine(output, filename)} ({report.summary.totalSize} bytes)");
    }
}
