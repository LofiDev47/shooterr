using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Command-line build entry point:
    /// Unity -batchmode -quit -projectPath . -executeMethod Shooter.EditorTools.ShooterBuild.BuildLinux
    /// Produces Builds/Linux/Shooter.x86_64 and exits with a non-zero code on failure.
    /// </summary>
    public static class ShooterBuild
    {
        private const string BuildDirectory = "Builds/Linux";
        private const string ExecutableName = "Shooter.x86_64";

        [MenuItem("Shooter/Build Linux Player")]
        public static void BuildLinux()
        {
            int exitCode = 0;

            try
            {
                ConfigurePlayerSettings();

                if (!File.Exists(ShooterSceneBuilder.ScenePath))
                {
                    Debug.Log("[Shooter] Arena scene missing, building it first.");
                    ShooterSceneBuilder.Build();
                }

                Directory.CreateDirectory(BuildDirectory);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ShooterSceneBuilder.ScenePath },
                    locationPathName = Path.Combine(BuildDirectory, ExecutableName),
                    target = BuildTarget.StandaloneLinux64,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                Debug.Log(
                    $"[Shooter] Build {summary.result}: {summary.totalSize} bytes, " +
                    $"{summary.totalErrors} errors, {summary.totalWarnings} warnings, " +
                    $"finished in {summary.totalTime}.");

                exitCode = summary.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Shooter] Build failed with an exception: {exception}");
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "LofiDev";
            PlayerSettings.productName = "Shooter";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[Shooter] Player settings configured (Shooter, 1600x900 windowed).");
        }
    }
}
