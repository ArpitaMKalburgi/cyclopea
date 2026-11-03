using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cyclopea.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Cyclopea.EditorTools
{
    /// <summary>
    /// Headless project setup, scene build, APK build and offline per-eye captures.
    ///   Unity -batchmode -projectPath . -executeMethod Cyclopea.EditorTools.CyclopeaSetup.Setup -quit
    ///   ... -executeMethod Cyclopea.EditorTools.CyclopeaSetup.BuildApk
    ///   ... -executeMethod Cyclopea.EditorTools.CyclopeaSetup.CaptureEyes   (no -nographics)
    /// Everything is also on the Cyclopea menu for use inside the editor.
    /// </summary>
    public static class CyclopeaSetup
    {
        const string Root = "Assets/Cyclopea";
        const string SettingsDir = Root + "/Settings";
        const string MaterialsDir = Root + "/Materials";
        const string ScenePath = Root + "/Scenes/Cyclopea.unity";
        const string PackageId = "com.ujwal240.cyclopea";

        // ---------- entry points ----------

        [MenuItem("Cyclopea/1. Configure project (URP, Android, Meta)")]
        public static void Setup()
        {
            var report = new StringBuilder();
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(MaterialsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            ConfigureUrp(report);
            ConfigurePlayer(report);
            ConfigureMetaProjectConfig(report);
            RunMetaFixes(report);
            ConfigureOpenXR(report);
            BuildScene(report);
            AssetDatabase.SaveAssets();
            report.AppendLine(CheckList());
            File.WriteAllText("Tools/setup-report.txt", report.ToString());
            Debug.Log("[Cyclopea] Setup complete\n" + report);
        }

        /// <summary>Rebuild the scene from code, then build the APK. The usual CLI entry point after code changes.</summary>
        public static void SceneAndApk()
        {
            var r = new StringBuilder();
            BuildScene(r);
            AssetDatabase.SaveAssets();
            Debug.Log("[Cyclopea] " + r);
            BuildApk();
        }

        [MenuItem("Cyclopea/2. Build dev APK")]
        public static void BuildApk()
        {
            Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Cyclopea-dev.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            };
            BuildReport r = BuildPipeline.BuildPlayer(options);
            string msg = $"[Cyclopea] Build {r.summary.result}: {r.summary.outputPath} " +
                         $"{r.summary.totalSize / (1024 * 1024)} MB, {r.summary.totalErrors} errors, {r.summary.totalTime}";
            Debug.Log(msg);
            File.WriteAllText("Tools/build-report.txt", msg + "\n");
            if (Application.isBatchMode && r.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        // ---------- URP ----------

        static void ConfigureUrp(StringBuilder report)
        {
            string rendererPath = SettingsDir + "/Cyclopea_Renderer.asset";
            string assetPath = SettingsDir + "/Cyclopea_URP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            // Quest: MSAA 4x keeps dot edges clean (sub-pixel disparity lives in edge pixels);
            // HDR off so passthrough alpha survives; no shadows, no depth/opaque copies.
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1f;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            var so = new SerializedObject(urp);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_UseSRPBatcher", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
            report.AppendLine("URP: " + assetPath + " (MSAA 4x, HDR off, shadows off)");
        }

        // ---------- Player ----------

        static void ConfigurePlayer(StringBuilder report)
        {
            PlayerSettings.companyName = "Ujwal240";
            PlayerSettings.productName = "Cyclopea";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.forceInternetPermission = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            // Accept both input backends: Meta's OVRInput and the Input System keyboard used in the editor.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var pso = new SerializedObject(ps);
                var input = pso.FindProperty("activeInputHandler");
                if (input != null) { input.intValue = 2; pso.ApplyModifiedPropertiesWithoutUndo(); }
            }
            report.AppendLine($"Player: {PackageId}, IL2CPP, ARM64, API 32+, Vulkan, Linear, ASTC");
        }

        // ---------- Meta project config (manifest features) ----------

        static void ConfigureMetaProjectConfig(StringBuilder report)
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            OVRProjectConfig.CommitProjectConfig(cfg);
            report.AppendLine("OVRProjectConfig: hands+controllers, passthrough, scene, anchors");
        }

        static void RunMetaFixes(StringBuilder report)
        {
            // OVRProjectSetup.FixTasks(..., blocking: true) is internal; the CLI uses the same blocking path.
            var t = typeof(OVRProjectSetup);
            var fix = t.GetMethod("FixTasks", BindingFlags.NonPublic | BindingFlags.Static);
            var logEnum = t.GetNestedType("LogMessages", BindingFlags.NonPublic);
            if (fix == null || logEnum == null)
            {
                report.AppendLine("Meta fixes: FixTasks not found (API changed) - run Project Setup Tool > Fix All in the editor");
                return;
            }
            object logAll = Enum.Parse(logEnum, "Changed");
            for (int pass = 0; pass < 3; pass++)
            {
                try
                {
                    fix.Invoke(null, new object[] { BuildTargetGroup.Android, null, logAll, true, null, false });
                }
                catch (Exception e)
                {
                    report.AppendLine("Meta fixes pass " + pass + " threw: " + (e.InnerException ?? e).Message);
                }
            }
            report.AppendLine("Meta Project Setup Tool: FixTasks(Android) x3");
        }

        // ---------- OpenXR ----------

        static void ConfigureOpenXR(StringBuilder report)
        {
            // Make sure OpenXR is the active loader on Android (Meta's helper is internal; call it by reflection).
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            bool hasLoader = general != null && general.Manager != null &&
                             general.Manager.activeLoaders.Any(l => l is OpenXRLoader);
            if (!hasLoader)
            {
                var tasks = typeof(OVRProjectSetup).Assembly.GetType("OVRProjectSetupXRTasks");
                var add = tasks?.GetMethod("AddLoader", BindingFlags.NonPublic | BindingFlags.Static);
                add?.MakeGenericMethod(typeof(OpenXRLoader)).Invoke(null, new object[] { BuildTargetGroup.Android });
                general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
                hasLoader = general != null && general.Manager.activeLoaders.Any(l => l is OpenXRLoader);
            }
            report.AppendLine("OpenXR loader on Android: " + hasLoader);

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                // The package settings asset is created lazily by the OpenXR UI; create it here (internal type).
                var pkgType = typeof(UnityEditor.XR.OpenXR.Features.FeatureHelpers).Assembly
                    .GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
                pkgType?.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.Invoke(null, null);
                AssetDatabase.SaveAssets();
                settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            }
            if (settings == null) { report.AppendLine("OpenXR settings: missing (open Project Settings > XR Plug-in Management > OpenXR once)"); return; }
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            // Multiview on Quest: one pass, both eyes; unity_StereoEyeIndex comes from gl_ViewID.
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            var wanted = new[] { "MetaXRFeature", "MetaQuestFeature", "HandTracking", "MetaHandTrackingAim",
                                 "OculusTouchControllerProfile" };
            var enabled = new List<string>();
            foreach (var f in settings.GetFeatures())
            {
                if (wanted.Contains(f.GetType().Name)) f.enabled = true;
                if (f.enabled) enabled.Add(f.GetType().Name);
            }
            EditorUtility.SetDirty(settings);
            report.AppendLine("OpenXR render mode: " + settings.renderMode);
            report.AppendLine("OpenXR features on: " + string.Join(", ", enabled));
        }

        // ---------- Scene ----------

        [MenuItem("Cyclopea/3. Rebuild scene")]
        public static void BuildSceneMenu() { var r = new StringBuilder(); BuildScene(r); Debug.Log(r); }

        static void BuildScene(StringBuilder report)
        {
            var rds = MakeMaterial(MaterialsDir + "/RDS.mat", Shader.Find("Cyclopea/RandomDotStereogram"), m =>
            {
                m.SetColor("_Background", new Color(0.10f, 0.10f, 0.12f, 1f));
                m.SetColor("_DotColor", new Color(0.92f, 0.92f, 0.92f, 1f));
                m.enableInstancing = true;
            });
            var frameMat = MakeMaterial(MaterialsDir + "/Frame.mat", Shader.Find("Sprites/Default"), null);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera rig with passthrough underlay.
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab"));
            rig.name = "OVRCameraRig";
            var cameraRig = rig.GetComponent<OVRCameraRig>();
            var manager = rig.GetComponent<OVRManager>() ?? rig.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            var mso = new SerializedObject(manager);
            SetBool(mso, "requestScenePermissionOnStartup", true);
            mso.ApplyModifiedPropertiesWithoutUndo();
            rig.AddComponent<OVRPassthroughLayer>();   // always an underlay in this SDK

            Transform center = cameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var cam = center.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.05f;
            var camData = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            camData.antialiasing = AntialiasingMode.None;

            // Hands (data only; the real hands are visible through passthrough).
            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab");
            OVRHand left = MakeHand(handPrefab, cameraRig.transform.Find("TrackingSpace/LeftHandAnchor"), true);
            OVRHand right = MakeHand(handPrefab, cameraRig.transform.Find("TrackingSpace/RightHandAnchor"), false);

            // Scene model (tables, walls).
            var mrukPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab");
            if (mrukPrefab != null)
            {
                // Real scan only. The prefab default falls back to a random sample room, which would put
                // the static on a table that isn't there; without a scan we use the easel instead.
                var mruk = (GameObject)PrefabUtility.InstantiatePrefab(mrukPrefab);
                var comp = mruk.GetComponent<Meta.XR.MRUtilityKit.MRUK>();
                var kso = new SerializedObject(comp);
                SetEnum(kso, "SceneSettings.DataSource", "Device");
                kso.ApplyModifiedPropertiesWithoutUndo();
            }

            // Game.
            var game = new GameObject("Cyclopea");
            var finder = game.AddComponent<SurfaceFinder>();
            finder.Head = center;

            var patches = new StereoPatch[4];
            var frames = new LineRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Patch" + (i + 1);
                q.transform.SetParent(game.transform, false);
                q.GetComponent<MeshRenderer>().sharedMaterial = rds;
                q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                patches[i] = q.AddComponent<StereoPatch>();

                var frameGo = new GameObject("Frame");
                frameGo.transform.SetParent(q.transform, false);
                var lr = frameGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = 4;
                const float e = 0.53f;
                lr.SetPositions(new[] { new Vector3(-e, -e, -0.002f), new Vector3(e, -e, -0.002f),
                                        new Vector3(e, e, -0.002f), new Vector3(-e, e, -0.002f) });
                lr.widthMultiplier = 0.0025f;
                lr.sharedMaterial = frameMat;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                frames[i] = lr;
            }

            var rays = new[] { MakeRay("LeftRay", frameMat, game.transform), MakeRay("RightRay", frameMat, game.transform) };
            var handRay = game.AddComponent<HandRaySelector>();
            handRay.LeftHand = left; handRay.RightHand = right;
            handRay.LeftRay = rays[0]; handRay.RightRay = rays[1];
            var dwell = game.AddComponent<HeadDwellSelector>();
            dwell.Head = center;
            dwell.enabled = false;
            var keys = game.AddComponent<KeyboardSelector>();
            keys.Patches = patches;
            keys.Cam = cam;

            var session = game.AddComponent<SpotSession>();
            session.Head = center;
            session.Surface = finder;
            session.Patches = patches;
            session.Frames = frames;
            session.Selectors = new PatchSelector[] { handRay, dwell, keys };
            session.Title = MakeText("Title", game.transform, 0.0011f, 72);
            session.Status = MakeText("Status", game.transform, 0.0007f, 56);
            session.Audio = game.AddComponent<AudioSource>();
            session.Audio.playOnAwake = false;
            session.Audio.spatialBlend = 0f;

            var dev = game.AddComponent<DevControls>();
            dev.LeftHand = left; dev.RightHand = right;
            dev.Session = session; dev.Dwell = dwell; dev.HandRay = handRay;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            report.AppendLine("Scene: " + ScenePath + " (rig, passthrough underlay, hands, MRUK, 4 patches, session)");
        }

        static OVRHand MakeHand(GameObject prefab, Transform anchor, bool isLeft)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            go.name = isLeft ? "LeftHand" : "RightHand";
            var hand = go.GetComponent<OVRHand>();
            var hso = new SerializedObject(hand);
            SetEnum(hso, "HandType", isLeft ? "HandLeft" : "HandRight");
            hso.ApplyModifiedPropertiesWithoutUndo();
            var skel = go.GetComponent<OVRSkeleton>();
            if (skel != null)
            {
                var sso = new SerializedObject(skel);
                SetEnum(sso, "_skeletonType", isLeft ? "HandLeft" : "HandRight");
                sso.ApplyModifiedPropertiesWithoutUndo();
            }
            var mesh = go.GetComponent<OVRMesh>();
            if (mesh != null)
            {
                var mso = new SerializedObject(mesh);
                SetEnum(mso, "_meshType", isLeft ? "HandLeft" : "HandRight");
                mso.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            return hand;
        }

        static LineRenderer MakeRay(string name, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.0015f;
            lr.sharedMaterial = mat;
            lr.startColor = new Color(1, 1, 1, 0.5f);
            lr.endColor = new Color(1, 1, 1, 0.05f);
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.enabled = false;
            return lr;
        }

        static TextMesh MakeText(string name, Transform parent, float charSize, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.fontSize = fontSize;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 1f, 1f, 0.92f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }

        static Material MakeMaterial(string path, Shader shader, Action<Material> init)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                if (shader == null) throw new Exception("Shader missing for " + path);
                m = new Material(shader);
                init?.Invoke(m);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        // ---------- Offline per-eye captures ----------

        /// <summary>
        /// Renders one patch as the left eye and as the right eye (shader _ForceEye) at several disparities
        /// and seeds, at two resolutions: 1024 px (math check) and 288 px (about what a 12 cm patch at
        /// 60 cm covers on Quest 3). Writes PNGs + manifest.json for Tools/eye_diff.py and Tools/mono_cue.py.
        /// </summary>
        [MenuItem("Cyclopea/4. Capture per-eye images (offline)")]
        public static void CaptureEyes()
        {
            string outDir = "Tools/captures";
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var shader = Shader.Find("Cyclopea/RandomDotStereogram");
            var mat = new Material(shader);
            mat.SetColor("_Background", new Color(0.10f, 0.10f, 0.12f, 1f));
            mat.SetColor("_DotColor", new Color(0.92f, 0.92f, 0.92f, 1f));

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.transform.position = new Vector3(0, 0, -1);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.magenta;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 5f;
            cam.allowMSAA = false;

            const double z = 0.6, patch = 0.12;
            double[] levels = { 1920, 480, 120, 60, 15, 0 };
            int[] sizes = { 1024, 288 };
            var manifest = new StringBuilder("[\n");
            bool first = true;
            var rng = new System.Random(7);
            int seeds = 40;

            foreach (int size in sizes)
            {
                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = size == 288 ? 4 : 1 };
                rt.Create();
                cam.targetTexture = rt;
                cam.aspect = 1f;
                foreach (double arcsec in levels)
                {
                    for (int s = 0; s < (arcsec == 1920 || arcsec == 15 || arcsec == 0 ? seeds : 4); s++)
                    {
                        float seed = (float)(rng.NextDouble() * 1000);
                        float cx = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.12f;
                        float cy = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.12f;
                        float wobble = 0.08f + (float)rng.NextDouble() * 0.14f;
                        float lobes = 3 + rng.Next(0, 4);
                        float offset = DisparityMath.EyeOffsetUV(arcsec, z, patch);
                        mat.SetFloat("_Seed", seed);
                        mat.SetVector("_MaskCenter", new Vector4(cx, cy, 0, 0));
                        mat.SetFloat("_MaskRadius", 0.27f);
                        mat.SetFloat("_MaskWobble", wobble);
                        mat.SetFloat("_MaskLobes", lobes);
                        mat.SetFloat("_CellSize", (float)(StereoPatch.CellSizeM / patch));
                        mat.SetFloat("_DotRadius", (float)(StereoPatch.DotRadiusM / patch));
                        mat.SetFloat("_EyeOffset", offset);
                        mat.SetFloat("_OffsetSign", 1f);
                        string stem = $"{size}_{arcsec:0}_{s:00}";
                        for (int eye = 0; eye < 2; eye++)
                        {
                            mat.SetFloat("_ForceEye", eye);
                            Render(cam, rt);
                            Save(rt, $"{outDir}/{stem}_{(eye == 0 ? "L" : "R")}.png");
                        }
                        if (!first) manifest.Append(",\n");
                        first = false;
                        manifest.Append($"  {{\"stem\":\"{stem}\",\"size\":{size},\"arcsec\":{arcsec},\"seed\":{seed:0.###}," +
                                        $"\"eye_offset_uv\":{offset:0.########},\"expect_px\":{offset * size:0.####}," +
                                        $"\"mask_center\":[{cx:0.####},{cy:0.####}],\"mask_radius\":0.27," +
                                        $"\"mask_wobble\":{wobble:0.####},\"mask_lobes\":{lobes},\"mask_phase\":{seed:0.###}}}");
                    }
                }
                cam.targetTexture = null;
                rt.Release();
            }
            manifest.Append("\n]\n");
            File.WriteAllText(outDir + "/manifest.json", manifest.ToString());
            UnityEngine.Object.DestroyImmediate(quad);
            UnityEngine.Object.DestroyImmediate(camGo);
            Debug.Log("[Cyclopea] Per-eye captures written to " + outDir);
        }

        static void Render(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
                cam.Render();
        }

        static void Save(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ---------- checks ----------

        static string CheckList()
        {
            var sb = new StringBuilder("\nChecks:\n");
            sb.AppendLine("  build target: " + EditorUserBuildSettings.activeBuildTarget);
            sb.AppendLine("  render pipeline: " + (GraphicsSettings.defaultRenderPipeline != null ? GraphicsSettings.defaultRenderPipeline.name : "none"));
            sb.AppendLine("  graphics APIs: " + string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)));
            sb.AppendLine("  min SDK: " + PlayerSettings.Android.minSdkVersion);
            sb.AppendLine("  backend: " + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            sb.AppendLine("  colour space: " + PlayerSettings.colorSpace);
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            sb.AppendLine("  XR loaders: " + (general?.Manager != null ? string.Join(",", general.Manager.activeLoaders.Select(l => l.GetType().Name)) : "none"));
            return sb.ToString();
        }

        static void SetBool(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = v;
            else Debug.LogWarning("[Cyclopea] property not found: " + prop);
        }

        static void SetEnum(SerializedObject so, string prop, string name)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[Cyclopea] property not found: " + prop); return; }
            int idx = Array.IndexOf(p.enumNames, name);
            if (idx >= 0) p.enumValueIndex = idx;
            else Debug.LogWarning($"[Cyclopea] enum value {name} not in {prop}: {string.Join(",", p.enumNames)}");
        }
    }
}
