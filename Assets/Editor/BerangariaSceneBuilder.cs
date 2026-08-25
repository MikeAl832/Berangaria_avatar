using System;
using System.IO;
using System.Linq;
using Berangaria.Avatar.Runtime;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Berangaria.Avatar.Editor
{
    public static class BerangariaSceneBuilder
    {
        private const string ModelAssetPath = "Assets/Characters/current.vrm";
        private const string SceneAssetPath = "Assets/Scenes/Berangaria.unity";

        [MenuItem("Berangaria/Replace Avatar VRM...")]
        public static void ReplaceAvatar()
        {
            var sourcePath = EditorUtility.OpenFilePanel("Choose VRM 1.0 avatar", "", "vrm");
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            var targetPath = Path.GetFullPath(Path.Combine(Application.dataPath, "Characters", "current.vrm"));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.Copy(sourcePath, targetPath, overwrite: true);
            AssetDatabase.ImportAsset(ModelAssetPath, ImportAssetOptions.ForceSynchronousImport);
            Build();
            OpenScene();
            Debug.Log($"BERANGARIA_AVATAR_REPLACED source={Path.GetFileName(sourcePath)}");
        }

        [MenuItem("Berangaria/Open Avatar Scene")]
        public static void OpenScene()
        {
            EditorSceneManager.OpenScene(SceneAssetPath, OpenSceneMode.Single);

            var character = GameObject.Find("Berangaria");
            if (character != null)
            {
                Selection.activeGameObject = character;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }

        [MenuItem("Berangaria/Open and Play Avatar")]
        public static void OpenAndPlay()
        {
            OpenScene();
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Berangaria/Validate Avatar Runtime")]
        public static void ValidateRuntimeScene()
        {
            OpenScene();

            var rig = UnityEngine.Object.FindObjectOfType<AvatarRig>();
            if (rig == null || rig.AvatarRoot == null)
            {
                throw new InvalidOperationException("Avatar Runtime or its replaceable avatar is missing.");
            }

            var vrm = rig.AvatarRoot.GetComponentInChildren<Vrm10Instance>(true);
            var animator = rig.AvatarRoot.GetComponentInChildren<Animator>(true);
            if (vrm == null || animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                throw new InvalidOperationException("The configured avatar is not a valid VRM humanoid.");
            }

            var expressions = string.Join(",", vrm.Vrm.Expression.Clips
                .Select(item => item.Preset.ToString())
                .Distinct()
                .OrderBy(name => name));
            Debug.Log(
                $"BERANGARIA_RUNTIME_SCENE_OK model={rig.AvatarRoot.name} " +
                $"humanoid={animator.avatar.isHuman} expressions=[{expressions}]");
        }

        [MenuItem("Berangaria/Build Avatar Scene")]
        public static void Build()
        {
            try
            {
                AssetDatabase.ImportAsset(ModelAssetPath, ImportAssetOptions.ForceUpdate);

                var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAssetPath);
                if (modelPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"UniVRM did not create a GameObject for {ModelAssetPath}.");
                }

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                scene.name = "Berangaria";

                var runtimeObject = new GameObject("Avatar Runtime");
                var character = UnityEngine.Object.Instantiate(modelPrefab);
                character.name = "Berangaria";
                character.transform.SetParent(runtimeObject.transform, false);
                character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var bounds = CalculateBounds(character);
                character.transform.position -= new Vector3(0f, bounds.min.y, 0f);
                bounds = CalculateBounds(character);

                var camera = CreateCamera(bounds);
                CreateLighting();
                CreateGround(bounds);

                var gazeTargetObject = new GameObject("Gaze Target");
                gazeTargetObject.transform.SetParent(camera.transform, false);

                var avatarRig = runtimeObject.AddComponent<AvatarRig>();
                avatarRig.Configure(character, gazeTargetObject.transform);
                var demoController = runtimeObject.AddComponent<AvatarDemoController>();
                demoController.Configure(avatarRig);
                var udpBridge = runtimeObject.AddComponent<AvatarUdpBridge>();
                udpBridge.Configure(avatarRig, demoController);

                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.42f, 0.46f, 0.56f);
                RenderSettings.ambientEquatorColor = new Color(0.20f, 0.22f, 0.28f);
                RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.12f);

                PlayerSettings.colorSpace = ColorSpace.Linear;
                PlayerSettings.productName = "Berangaria Avatar";
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, SceneAssetPath);

                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(SceneAssetPath, true),
                };

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"BERANGARIA_SCENE_READY model={ModelAssetPath} scene={SceneAssetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("The imported avatar has no renderers.");
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static Camera CreateCamera(Bounds bounds)
        {
            // Conversational framing: head and upper body stay large enough to read expressions.
            var target = new Vector3(
                bounds.center.x,
                bounds.min.y + bounds.size.y * 0.69f,
                bounds.center.z);
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.042f, 0.060f);
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 100f;

            var distance = Mathf.Max(1.0f, bounds.size.y * 1.14f);
            cameraObject.transform.position = target + new Vector3(0f, 0f, distance);
            cameraObject.transform.LookAt(target);
            return camera;
        }

        private static void CreateLighting()
        {
            var keyObject = new GameObject("Key Light");
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.88f, 0.82f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.Soft;
            keyObject.transform.rotation = Quaternion.Euler(32f, 145f, 0f);

            var fillObject = new GameObject("Fill Light");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.55f, 0.70f, 1f);
            fill.intensity = 0.55f;
            fill.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(18f, -45f, 0f);
        }

        private static void CreateGround(Bounds bounds)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(bounds.center.x, 0f, bounds.center.z);
            var scale = Mathf.Max(0.5f, bounds.size.y * 0.22f);
            ground.transform.localScale = new Vector3(scale, 1f, scale);

            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                return;
            }

            var material = new Material(shader)
            {
                name = "Ground Material",
                color = new Color(0.08f, 0.09f, 0.12f),
            };
            ground.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
