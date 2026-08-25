using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Berangaria.Avatar.Editor
{
    public static class BerangariaReleaseBuilder
    {
        private const string SceneAssetPath = "Assets/Scenes/Berangaria.unity";
        private const string OutputPath = "Builds/Windows/BerangariaAvatar.exe";

        [MenuItem("Berangaria/Build Windows Avatar")]
        public static void BuildWindows()
        {
            BerangariaSceneBuilder.Build();

            PlayerSettings.productName = "Berangaria Avatar";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;

            var absoluteOutputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputPath));
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SceneAssetPath },
                locationPathName = absoluteOutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows avatar build failed: {report.summary.result} " +
                    $"errors={report.summary.totalErrors}");
            }

            Debug.Log(
                $"BERANGARIA_WINDOWS_BUILD_OK output={absoluteOutputPath} " +
                $"bytes={report.summary.totalSize}");
        }
    }
}
