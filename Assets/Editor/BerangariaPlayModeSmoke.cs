using System;
using System.IO;
using Berangaria.Avatar.Runtime;
using UnityEditor;
using UnityEngine;

namespace Berangaria.Avatar.Editor
{
    [InitializeOnLoad]
    public static class BerangariaPlayModeSmoke
    {
        private const string StageKey = "Berangaria.PlayModeSmoke.Stage";
        private const string StartedKey = "Berangaria.PlayModeSmoke.Started";
        private const string Waiting = "waiting";
        private const string Running = "running";
        private const string Exiting = "exiting";

        static BerangariaPlayModeSmoke()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("Berangaria/Run Play Mode Smoke")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Stop Play Mode before starting the avatar smoke test.");
            }

            BerangariaSceneBuilder.OpenScene();
            SessionState.SetString(StageKey, Waiting);
            SessionState.SetFloat(StartedKey, 0f);
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            var stage = SessionState.GetString(StageKey, string.Empty);
            if (string.IsNullOrEmpty(stage))
            {
                return;
            }

            if (stage == Waiting)
            {
                if (!EditorApplication.isPlaying)
                {
                    return;
                }

                SessionState.SetString(StageKey, Running);
                SessionState.SetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
                return;
            }

            if (stage == Running)
            {
                if (!EditorApplication.isPlaying)
                {
                    Finish(1, "Play Mode ended before the avatar became ready.");
                    return;
                }

                var elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartedKey, 0f);
                // The demo cycles Idle -> Listening -> Thinking -> Speaking every 4.5 seconds.
                // Capturing Speaking proves that the full state cycle and lip-sync both ran.
                if (elapsed < 14.6f)
                {
                    return;
                }

                var rig = UnityEngine.Object.FindObjectOfType<AvatarRig>();
                if (rig == null || !rig.IsReady || Camera.main == null)
                {
                    if (elapsed < 22f)
                    {
                        return;
                    }

                    Finish(1, "AvatarRig or Main Camera did not become ready.");
                    return;
                }

                try
                {
                    var previewPath = CapturePreview(Camera.main);
                    Debug.Log(
                        $"BERANGARIA_PLAYMODE_SMOKE_OK state={rig.State} " +
                        $"emotion={rig.Emotion} speech={rig.SpeechLevel:0.000} preview={previewPath}");
                    Finish(0, null);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Finish(1, exception.Message);
                }

                return;
            }

            if (stage == Exiting && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var exitCode = SessionState.GetInt(StageKey + ".ExitCode", 1);
                ClearSession();
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(exitCode);
                }
            }
        }

        private static string CapturePreview(Camera camera)
        {
            const int width = 1280;
            const int height = 720;
            var previewPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Logs", "avatar-runtime-preview.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath));

            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(previewPath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            return previewPath;
        }

        private static void Finish(int exitCode, string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"BERANGARIA_PLAYMODE_SMOKE_FAILED {error}");
            }

            SessionState.SetInt(StageKey + ".ExitCode", exitCode);
            SessionState.SetString(StageKey, Exiting);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static void ClearSession()
        {
            SessionState.EraseString(StageKey);
            SessionState.EraseFloat(StartedKey);
            SessionState.EraseInt(StageKey + ".ExitCode");
        }
    }
}
