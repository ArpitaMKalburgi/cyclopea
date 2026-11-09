using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cyclopea.Core;
using Meta.XR;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
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
            ActivateXrOperator(report);
            RunMetaFixes(report);
            ConfigureOpenXR(report);
            ConfigurePlatform(report);
            ConfigureAudio(report);
            SetImmersiveDebugger(true, report);
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
            var sb = new StringBuilder();
            SetImmersiveDebugger(true, sb);
            Build("Builds/Cyclopea-dev.apk", BuildOptions.Development, "Tools/build-report.txt");
        }

        /// <summary>
        /// The build judges get: release (no Development flag, so Meta's build processor drops the XR
        /// Operator AAR and its screen-capture permission), Immersive Debugger off, finger switches off.
        /// </summary>
        [MenuItem("Cyclopea/2b. Build judge APK (release)")]
        public static void BuildJudgeApk()
        {
            var sb = new StringBuilder();
            SetImmersiveDebugger(false, sb);
            try { Build("Builds/Cyclopea-judge.apk", BuildOptions.None, "Tools/build-judge-report.txt"); }
            finally { SetImmersiveDebugger(true, sb); AssetDatabase.SaveAssets(); }
        }

        static void Build(string path, BuildOptions opts, string reportPath)
        {
            Directory.CreateDirectory("Builds");
            AssetDatabase.SaveAssets();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = opts
            };
            BuildReport r = BuildPipeline.BuildPlayer(options);
            string msg = $"[Cyclopea] Build {r.summary.result}: {r.summary.outputPath} " +
                         $"{(File.Exists(path) ? new FileInfo(path).Length : 0) / (1024 * 1024)} MB, {r.summary.totalErrors} errors, {r.summary.totalTime}";
            Debug.Log(msg);
            File.WriteAllText(reportPath, msg + "\n");
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
            cfg.isPassthroughCameraAccessEnabled = true;   // HEADSET_CAMERA in the manifest; asked for lazily (round 2)
            OVRProjectConfig.CommitProjectConfig(cfg);
            report.AppendLine("OVRProjectConfig: hands+controllers, passthrough, scene, anchors, passthrough camera access");
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
            // HandInteractionProfile: lets Meta XR Operator inject synthetic hands (agent-driven device tests).
            var wanted = new[] { "MetaXRFeature", "MetaQuestFeature", "HandTracking", "MetaHandTrackingAim",
                                 "OculusTouchControllerProfile", "HandInteractionProfile" };
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

        // ---------- Meta XR Audio ----------

        /// <summary>Meta XR Audio as the project's spatializer (HRTF + room acoustics for the creature's voice).</summary>
        static void ConfigureAudio(StringBuilder report)
        {
            const string name = "Meta XR Audio";
            bool available = AudioSettings.GetSpatializerPluginNames().Contains(name);
            if (available) AudioSettings.SetSpatializerPluginName(name);
            report.AppendLine("Spatializer: " + (available ? name : "Meta XR Audio not found, Unity default panning") +
                              " (current: " + AudioSettings.GetSpatializerPluginName() + ")");
        }

        // ---------- Platform SDK + Immersive Debugger ----------

        const string AppId = "1408927408970808";   // public App ID (Developer Dashboard), not a secret

        static void ConfigurePlatform(StringBuilder report)
        {
            Oculus.Platform.PlatformSettings.MobileAppID = AppId;
            foreach (var guid in AssetDatabase.FindAssets("t:PlatformSettings"))
                EditorUtility.SetDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
            report.AppendLine("Platform SDK: Quest App ID " + AppId);
        }

        /// <summary>Meta's Immersive Debugger: on for dev builds (in-headset tuning panel), off for the judge build.</summary>
        static void SetImmersiveDebugger(bool on, StringBuilder report)
        {
            Type t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Meta.XR.ImmersiveDebugger.RuntimeSettings")).FirstOrDefault(x => x != null);
            var inst = t?.GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var prop = t?.GetProperty("ImmersiveDebuggerEnabled", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (inst == null || prop == null) { report.AppendLine("Immersive Debugger: settings API not found"); return; }
            prop.SetValue(inst, on);
            // Belt and braces: even if it were left on, the panel never appears in a release build.
            var so = new SerializedObject((UnityEngine.Object)inst);
            SetBool(so, "enableOnlyInDebugBuild", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty((UnityEngine.Object)inst);
            AssetDatabase.SaveAssets();
            report.AppendLine("Immersive Debugger: " + (on ? "on" : "off"));
        }

        /// <summary>
        /// Meta XR Operator (Core v207, OpenXR 1.17+): an OpenXR API layer with an MCP server inside
        /// development builds, so an AI agent can inject hands, gaze and pinches and capture each eye.
        /// Release builds exclude it automatically (MetaXROperatorBuildProcessor).
        /// </summary>
        static void ActivateXrOperator(StringBuilder report)
        {
            Type t = FindType("Meta.XR.Editor.MetaXROperatorActivator");
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            if (t == null) { report.AppendLine("XR Operator: activator not found"); return; }
            object present = t.GetMethod("AreBinariesPresent", F)?.Invoke(null, null);
            try { t.GetMethod("Activate", F)?.Invoke(null, new object[] { "cyclopea-setup" }); }
            catch (Exception e) { report.AppendLine("XR Operator: activate threw " + (e.InnerException ?? e).Message); }
            object active = t.GetProperty("IsActivated", F)?.GetValue(null);
            report.AppendLine($"XR Operator: editor binaries present {present}, activated {active}");
        }

        // ---------- Interaction SDK ----------

        static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName)).FirstOrDefault(x => x != null);

        static void NoEditableCopy(object wizard) =>
            wizard.GetType().GetMethod("InjectOptionalGenerateAsEditableCopy",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(wizard, new object[] { false });

        /// <summary>
        /// Adds Meta's OVR Comprehensive Interaction Rig (hand rays, poke, grab, ISDK hand visuals) the
        /// same way the editor's Building Block does, through its internal wizard.
        /// </summary>
        static GameObject AddInteractionRig(OVRCameraRig cameraRig, Material handMask, StringBuilder report)
        {
            Type wizard = FindType("Oculus.Interaction.OVR.Editor.QuickActions.OVRComprehensiveInteractionRigWizard");
            Type baseWizard = FindType("Oculus.Interaction.Editor.QuickActions.QuickActionsWizard");
            var create = baseWizard?.GetMethod("CreateWithDefaults", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (wizard == null || create == null) { report.AppendLine("ISDK rig: wizard API not found"); return null; }
            var inject = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(wizard),
                typeof(CyclopeaSetup).GetMethod(nameof(NoEditableCopy), BindingFlags.NonPublic | BindingFlags.Static));
            var made = ((IEnumerable<GameObject>)create.MakeGenericMethod(wizard).Invoke(null, new object[] { null, true, inject })).ToList();
            GameObject rig = made.FirstOrDefault(g => g != null && g.GetComponentInChildren<OVRCameraRigRef>(true) != null)
                             ?? made.FirstOrDefault();
            if (rig == null) { report.AppendLine("ISDK rig: wizard created nothing"); return null; }

            foreach (var r in rig.GetComponentsInChildren<OVRCameraRigRef>(true)) r.InjectAllOVRCameraRigRef(cameraRig, true);

            // Seated, stationary game: no locomotion of any kind (teleport, turn, microgesture locomotion).
            var off = new List<string>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.Contains("Locomot") || n.Contains("Teleport") || n.Contains("Turner") || n.Contains("Microgesture"))
                {
                    if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); off.Add(n); }
                }
            }
            // Mixed reality: the real hands are seen through passthrough. ISDK's hand mesh becomes a depth
            // mask, so the static is cut away wherever a real hand is in front of it.
            int masked = 0;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.name.Contains("Hand") && !smr.transform.parent.name.Contains("Hand")) continue;
                smr.sharedMaterials = Enumerable.Repeat(handMask, Math.Max(1, smr.sharedMaterials.Length)).ToArray();
                smr.shadowCastingMode = ShadowCastingMode.Off;
                masked++;
            }
            report.AppendLine($"ISDK rig: {rig.name} ({made.Count} objects), locomotion off: [{string.Join(", ", off)}], hand meshes masked: {masked}");
            return rig;
        }

        /// <summary>ISDK interactables on one square: hand ray target and gaze target over the quad's collider.</summary>
        static IsdkPatchEvents AddInteractables(StereoPatch patch)
        {
            var go = patch.gameObject;
            // A quad gets a non-convex MeshCollider, and ISDK's gaze conecast calls Collider.ClosestPoint on it,
            // which Unity refuses with a warning every frame (seen on device Nov 9: ~7k warnings in seconds).
            // A thin box gives the same hits for rays and gaze and supports ClosestPoint.
            Collider col = go.GetComponent<Collider>();
            if (col is MeshCollider mc)
            {
                UnityEngine.Object.DestroyImmediate(mc);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(1f, 1f, 0.002f);
                col = box;
            }
            var surface = go.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(col);
            var ray = go.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            var gaze = go.AddComponent<GazeInteractable>();
            gaze.InjectAllGazeInteractable(surface);
            var ev = go.AddComponent<IsdkPatchEvents>();
            ev.Patch = patch; ev.Ray = ray; ev.Gaze = gaze;
            return ev;
        }

        /// <summary>ISDK GazeInteractor on head gaze, selecting with a pinch of either hand ("look and pinch").</summary>
        static GazeInteractor AddGazeInteractor(Transform parent, Transform head, OVRHand left, OVRHand right)
        {
            var go = new GameObject("GazeInteractor (head gaze + pinch)");
            go.transform.SetParent(parent, false);
            var gazeSource = go.AddComponent<HeadGaze>();
            gazeSource.Head = head;
            var pinch = go.AddComponent<EitherHandPinch>();
            pinch.LeftHand = left; pinch.RightHand = right;
            var cone = go.AddComponent<GazeConecaster>();
            cone.InjectAllGazeConecaster(gazeSource);
            var interactor = go.AddComponent<GazeInteractor>();
            interactor.InjectAllGazeInteractor(pinch, head, cone);
            return interactor;
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
            // The look (Nov 9 art pass): rounded squares with a soft rim, deep ink ground, warm cream dots.
            // Set every rebuild, so an older material picks it up too.
            rds.SetColor("_Background", new Color(0.07f, 0.08f, 0.14f, 1f));
            rds.SetColor("_DotColor", new Color(0.98f, 0.92f, 0.80f, 1f));
            rds.SetFloat("_CornerRadius", PatchCornerRadius);
            rds.SetColor("_RimColor", new Color(0.55f, 0.75f, 1f, 0.22f));
            rds.SetFloat("_RimWidth", 0.06f);
            EditorUtility.SetDirty(rds);
            var frameMat = MakeMaterial(MaterialsDir + "/Frame.mat", Shader.Find("Sprites/Default"), null);
            var handMask = MakeMaterial(MaterialsDir + "/HandMask.mat", Shader.Find("Cyclopea/HandOccluder"), null);
            var panelMat = MakeMaterial(MaterialsDir + "/Panel.mat", Shader.Find("Cyclopea/RoundedPanel"), null);

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
            SetBool(mso, "requestPassthroughCameraAccessPermissionOnStartup", false);   // asked after round 1
            mso.ApplyModifiedPropertiesWithoutUndo();
            var passthrough = rig.AddComponent<OVRPassthroughLayer>();   // always an underlay in this SDK

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
            var leftGestures = left.gameObject.AddComponent<OVRMicrogestureEventSource>();
            leftGestures.Hand = left;
            var rightGestures = right.gameObject.AddComponent<OVRMicrogestureEventSource>();
            rightGestures.Hand = right;
            AddInteractionRig(cameraRig, handMask, report);

            // Scene model (tables, walls).
            var mrukPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab");
            var depthGo = new GameObject("EnvironmentRaycast (Depth API)");
            var depth = depthGo.AddComponent<EnvironmentRaycastManager>();
            var cameraGo = new GameObject("PassthroughCamera (round 2 camouflage)");
            var cameraAccess = cameraGo.AddComponent<PassthroughCameraAccess>();
            cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            cameraAccess.RequestedResolution = new Vector2Int(640, 480);
            cameraAccess.enabled = false;
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
            finder.Depth = depth;

            var patches = new StereoPatch[4];
            var isdkEvents = new IsdkPatchEvents[4];
            var frames = new LineRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Patch" + (i + 1);
                q.transform.SetParent(game.transform, false);
                q.GetComponent<MeshRenderer>().sharedMaterial = rds;
                q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                patches[i] = q.AddComponent<StereoPatch>();
                isdkEvents[i] = AddInteractables(patches[i]);

                var frameGo = new GameObject("Frame");
                frameGo.transform.SetParent(q.transform, false);
                var lr = frameGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                // Rounded to follow the square's corners, a little outside them.
                var outline = RoundedRect(0.53f, PatchCornerRadius + 0.03f, 6, -0.002f);
                lr.positionCount = outline.Length;
                lr.SetPositions(outline);
                lr.numCornerVertices = 2;
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
            AddGazeInteractor(game.transform, center, left, right);
            var isdk = game.AddComponent<IsdkSelector>();
            isdk.Patches = isdkEvents;
            var micro = game.AddComponent<MicrogestureSelector>();
            micro.Left = leftGestures; micro.Right = rightGestures; micro.Patches = patches;

            var session = game.AddComponent<SpotSession>();
            session.Head = center;
            session.Surface = finder;
            session.Patches = patches;
            session.Frames = frames;
            var voice = game.AddComponent<VoiceSelector>();
            voice.Patches = patches; voice.Session = session;
            session.Selectors = new PatchSelector[] { isdk, handRay, dwell, micro, voice, keys };
            session.Title = MakeText("Title", game.transform, 0.0011f, 72);
            session.Status = MakeText("Status", game.transform, 0.001f, 56);
            // Grow upward from where they are placed, so a longer block never sinks into the desk.
            session.Title.anchor = TextAnchor.LowerCenter;
            session.Status.anchor = TextAnchor.LowerCenter;
            // Readable on a real desk: sized from the distance to the head (first device run: 7–10 dmm, too small).
            var titleLegible = session.Title.gameObject.AddComponent<LegibleText>();
            titleLegible.Head = center; titleLegible.TargetDmm = 18f; titleLegible.MaxWidthDeg = 24f;
            var statusLegible = session.Status.gameObject.AddComponent<LegibleText>();
            statusLegible.Head = center; statusLegible.TargetDmm = 16f; statusLegible.MaxWidthDeg = 30f;
            // Words sit on soft dark panels: readable over any room, and they look like one product.
            session.Title.gameObject.AddComponent<TextPanel>().Material = panelMat;
            session.Status.gameObject.AddComponent<TextPanel>().Material = panelMat;
            session.Audio = game.AddComponent<AudioSource>();
            session.Audio.playOnAwake = false;
            session.Audio.spatialBlend = 0f;

            // Sounds: Assets/Cyclopea/Audio (Tools/make_sounds.py). Missing files leave the slot empty (plain tones).
            var sounds = game.AddComponent<SoundBank>();
            FillSoundBank(sounds, report);
            session.Sounds = sounds;

            // The static's soft hiss, from the squares (spatialised, quiet, fades with the static).
            var bedGo = new GameObject("StaticBed (Meta XR Audio)");
            bedGo.transform.SetParent(game.transform, false);
            var bed = bedGo.AddComponent<AudioSource>();
            bed.playOnAwake = false;
            bed.loop = true;
            bed.volume = 0f;
            bed.clip = sounds.StaticBed;
            bed.spatialBlend = 1f;
            bed.spatialize = true;
            bed.rolloffMode = AudioRolloffMode.Logarithmic;
            bed.minDistance = 0.4f; bed.maxDistance = 5f;
            var bedMxa = bedGo.AddComponent<MetaXRAudioSource>();
            bedMxa.EnableSpatialization = true;
            session.Bed = bed;

            // The creature's voice: a spatialised source moved to where the creature floated.
            var voiceGo = new GameObject("CreatureVoice (Meta XR Audio)");
            voiceGo.transform.SetParent(game.transform, false);
            var creatureVoice = voiceGo.AddComponent<AudioSource>();
            creatureVoice.playOnAwake = false;
            creatureVoice.spatialBlend = 1f;
            creatureVoice.spatialize = true;
            creatureVoice.rolloffMode = AudioRolloffMode.Logarithmic;
            creatureVoice.minDistance = 0.3f; creatureVoice.maxDistance = 6f;
            var mxa = voiceGo.AddComponent<MetaXRAudioSource>();
            mxa.EnableSpatialization = true;
            mxa.EnableAcoustics = true;
            session.CreatureVoice = creatureVoice;

            // Found creatures: out of the static, onto your fingertip (ISDK hand joints), into the nest.
            var creatureMat = MakeMaterial(MaterialsDir + "/Creature.mat", Shader.Find("Cyclopea/Creature"), null);
            var tips = game.AddComponent<FingerTips>();
            var creatures = game.AddComponent<CreatureDirector>();
            creatures.Material = creatureMat;
            creatures.Head = center;
            creatures.Tips = tips;
            creatures.Patches = patches;
            creatures.Voice = creatureVoice;
            creatures.Sounds = sounds;
            session.Creatures = creatures;

            // Reverb shaped like the player's own scanned room.
            var roomGo = new GameObject("RoomAcoustics (Meta XR Audio + MRUK)");
            var roomProps = roomGo.AddComponent<MetaXRAudioRoomAcousticProperties>();
            var roomAcoustics = roomGo.AddComponent<RoomAcoustics>();
            roomAcoustics.Room = roomProps;

            var dev = game.AddComponent<DevControls>();
            dev.LeftHand = left; dev.RightHand = right;
            dev.Session = session; dev.Dwell = dwell; dev.HandRay = handRay; dev.Isdk = isdk;
            handRay.enabled = false;   // backup path; DevControls switches it on if ISDK misbehaves

            var mood = game.AddComponent<PassthroughMood>();
            mood.Layer = passthrough; mood.Session = session;
            var camo = game.AddComponent<DeskCamouflage>();
            camo.Session = session; camo.CameraAccess = cameraAccess;
            var scores = game.AddComponent<ScoreAnchor>();
            scores.Session = session; scores.Head = center;
            scores.LineMaterial = frameMat;
            scores.PanelMaterial = panelMat;
            // Home card: the daily Depth Check's hub, its buttons are the four squares (every input works).
            var home = game.AddComponent<HomeCard>();
            home.Session = session; home.Scores = scores; home.Dev = dev;
            session.Home = home;
            // Each new kind moves onto a real surface from the room scan (beside the squares without one) and stays.
            var colony = game.AddComponent<RoomColony>();
            colony.Session = session; colony.Creatures = creatures; colony.Surface = finder;
            colony.Head = center; colony.Material = creatureMat;
            home.Room = colony;
            var platform = game.AddComponent<PlatformServices>();
            platform.Session = session; platform.Scores = scores;
            // Created on the dashboard Nov 4: first_creature, camouflage_round, streak_7 (simple, client
            // authoritative) and the days_in_a_row leaderboard (higher is better, not user facing).
            // Every call fails quietly until the Data Use Checkup is approved, so on is safe.
            platform.UseAchievements = true; platform.UseLeaderboard = true;
            var tuning = game.AddComponent<TuningPanel>();
            tuning.Session = session; tuning.Mood = mood; tuning.Camouflage = camo; tuning.Scores = scores;
            tuning.Platform = platform; tuning.Surface = finder; tuning.Acoustics = roomAcoustics;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            report.AppendLine("Scene: " + ScenePath + " (rig, passthrough underlay, hands, ISDK rig + gaze, microgestures, MRUK, " +
                              "depth raycast, passthrough camera, 4 patches, session, creatures + fingertips, anchor, home card, platform, tuning)");
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
            tm.color = new Color(1f, 0.97f, 0.92f, 0.95f);   // warm white, like the dots
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }

        /// <summary>Squares' corner radius (UV units of the 1×1 quad); the frames follow it.</summary>
        const float PatchCornerRadius = 0.07f;

        /// <summary>Closed rounded-square outline, half-extent <paramref name="e"/>, corner radius <paramref name="r"/>.</summary>
        static Vector3[] RoundedRect(float e, float r, int segs, float z)
        {
            var pts = new List<Vector3>();
            var corners = new[] { new Vector2(e - r, -e + r), new Vector2(e - r, e - r), new Vector2(-e + r, e - r), new Vector2(-e + r, -e + r) };
            for (int c = 0; c < 4; c++)
            {
                float a0 = -90f + 90f * c;
                for (int s = 0; s <= segs; s++)
                {
                    float a = (a0 + 90f * s / segs) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(corners[c].x + r * Mathf.Cos(a), corners[c].y + r * Mathf.Sin(a), z));
                }
            }
            return pts.ToArray();
        }

        const string AudioDir = "Assets/Cyclopea/Audio";

        static void FillSoundBank(SoundBank b, StringBuilder report)
        {
            int found = 0, missing = 0;
            AudioClip Load(string name)
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/" + name + ".wav");
                if (c != null) found++; else missing++;
                return c;
            }
            b.StaticBed = Load("static_bed");
            b.PickRight = Load("pick_right"); b.PickWrong = Load("pick_wrong");
            b.CreatureCondense = Load("creature_condense"); b.CreatureHop = Load("creature_hop");
            b.CreatureDive = Load("creature_dive"); b.CreaturePeek = Load("creature_peek");
            b.NewKind = Load("new_kind"); b.PersonalBest = Load("personal_best"); b.ScoreReveal = Load("score_reveal");
            b.LevelUp = Load("level_up"); b.UiHover = Load("ui_hover"); b.UiSelect = Load("ui_select");
            b.BreakChime = Load("break_chime"); b.EyeClose = Load("eye_close"); b.EyeOpen = Load("eye_open");
            b.CatchHold = Load("catch_hold"); b.CatchSuccess = Load("catch_success");
            var chirps = new List<AudioClip>();
            for (int i = 1; i <= 4; i++) { var c = Load("creature_chirp_" + i); if (c != null) chirps.Add(c); }
            b.CreatureChirps = chirps.ToArray();
            report.AppendLine("Sounds: " + found + " loaded from " + AudioDir + (missing > 0 ? ", " + missing + " missing (plain tones)" : ""));
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

        /// <summary>
        /// Motion-parallax mode through the production shader: the same patch rendered with the head
        /// 3 cm left (A) and 3 cm right (B) of centre, left eye, plus the right eye at A. The creature's
        /// dots must slide by the commanded amount between A and B, and both eyes must be identical.
        /// Writes Tools/captures-motion/ for Tools/verify_motion.py.
        /// </summary>
        [MenuItem("Cyclopea/4b. Capture motion parallax (offline)")]
        public static void CaptureMotion()
        {
            string outDir = "Tools/captures-motion";
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            var mat = new Material(Shader.Find("Cyclopea/RandomDotStereogram"));
            mat.SetColor("_Background", new Color(0.10f, 0.10f, 0.12f, 1f));
            mat.SetColor("_DotColor", new Color(0.92f, 0.92f, 0.92f, 1f));
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 0.5f;
            cam.transform.position = new Vector3(0, 0, -1);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.magenta;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 5f; cam.allowMSAA = false;
            const int size = 1024;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt; cam.aspect = 1f;

            const double z = 0.6, patch = 0.12, sway = 0.03;
            var manifest = new StringBuilder("[\n");
            bool first = true;
            var rng = new System.Random(19);
            foreach (double level in new[] { 1920.0, 480.0, 0.0 })
            for (int k = 0; k < 6; k++)
            {
                float seed = (float)(rng.NextDouble() * 1000);
                float cx = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.12f, cy = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.12f;
                float wobble = 0.08f + (float)rng.NextDouble() * 0.14f, lobes = 3 + rng.Next(0, 4);
                mat.SetFloat("_Seed", seed);
                mat.SetVector("_MaskCenter", new Vector4(cx, cy, 0, 0));
                mat.SetFloat("_MaskRadius", 0.27f); mat.SetFloat("_MaskWobble", wobble); mat.SetFloat("_MaskLobes", lobes);
                mat.SetFloat("_CellSize", (float)(StereoPatch.CellSizeM / patch));
                mat.SetFloat("_DotRadius", (float)(StereoPatch.DotRadiusM / patch));
                mat.SetFloat("_EyeOffset", 0f); mat.SetFloat("_OffsetSign", 1f);
                double depth = DisparityMath.PopOutMeters(level, z);
                // Same expression as StereoPatch.MotionShiftUV.
                float csA = (float)(-DisparityMath.MotionShiftMeters(-sway, depth, z) / patch);
                float csB = (float)(-DisparityMath.MotionShiftMeters(+sway, depth, z) / patch);
                string stem = $"{size}_{level:0}_{k:00}";
                void Shot(float cs, int eye, string tag)
                {
                    mat.SetFloat("_CommonShift", cs);
                    mat.SetFloat("_ForceEye", eye);
                    Render(cam, rt);
                    Save(rt, $"{outDir}/{stem}_{tag}.png");
                }
                Shot(csA, 0, "A"); Shot(csB, 0, "B"); Shot(csA, 1, "AR");
                if (!first) manifest.Append(",\n");
                first = false;
                // Content is displayed at -shift, so B relative to A slides by -(csB - csA) * size pixels.
                manifest.Append($"  {{\"stem\":\"{stem}\",\"size\":{size},\"arcsec\":{level},\"depth_m\":{depth:0.######}," +
                                $"\"sway_m\":{sway},\"expect_px\":{-(csB - csA) * size:0.####}," +
                                $"\"mask_center\":[{cx:0.####},{cy:0.####}],\"mask_radius\":0.27," +
                                $"\"mask_wobble\":{wobble:0.####},\"mask_lobes\":{lobes},\"mask_phase\":{seed:0.###}}}");
            }
            manifest.Append("\n]\n");
            File.WriteAllText(outDir + "/manifest.json", manifest.ToString());
            cam.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(quad);
            UnityEngine.Object.DestroyImmediate(camGo);
            Debug.Log("[Cyclopea] Motion captures written to " + outDir);
        }

        /// <summary>
        /// Offline look at the found creatures as a seated player sees them: four squares of static flat
        /// on a desk, one creature condensing out of its square, and a nest of all four species at four
        /// rarities. Plus a close-up. Mono render, so (correctly) no hidden shape shows in the static.
        /// </summary>
        [MenuItem("Cyclopea/4c. Capture creatures (offline)")]
        public static void CaptureCreatures()
        {
            string outDir = "Tools/captures-creatures";
            Directory.CreateDirectory(outDir);
            var shader = Shader.Find("Cyclopea/RandomDotStereogram");
            var creatureMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsDir + "/Creature.mat")
                              ?? new Material(Shader.Find("Cyclopea/Creature"));
            var made = new List<UnityEngine.Object>();
            var rng = new System.Random(7);
            const float size = 0.12f, step = (0.12f + 0.035f) * 0.5f;
            Vector3 center = new Vector3(0f, 0f, 0.6f);
            var flat = Quaternion.LookRotation(Vector3.down, Vector3.forward);   // static facing up, +v away from the player
            var patches = new List<Transform>();
            Vector2[] grid = { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1) };
            foreach (var g in grid)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                made.Add(q);
                var m = new Material(shader);
                made.Add(m);
                m.SetColor("_Background", new Color(0.10f, 0.10f, 0.12f, 1f));
                m.SetColor("_DotColor", new Color(0.92f, 0.92f, 0.92f, 1f));
                m.SetFloat("_Seed", (float)(rng.NextDouble() * 1000));
                m.SetFloat("_CellSize", StereoPatch.CellSizeM / size);
                m.SetFloat("_DotRadius", StereoPatch.DotRadiusM / size);
                m.SetFloat("_EyeOffset", 0f);
                q.GetComponent<MeshRenderer>().sharedMaterial = m;
                q.transform.SetPositionAndRotation(center + new Vector3(g.x * step, 0f, g.y * step), flat);
                q.transform.localScale = new Vector3(size, size, 1f);
                patches.Add(q.transform);
            }

            var camGo = new GameObject("CaptureCam");
            made.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.36f, 0.30f);   // stand-in for a wooden desk in passthrough
            cam.fieldOfView = 34f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 5f; cam.allowMSAA = true;
            var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt; cam.aspect = 1280f / 960f;

            CreatureBody Make(Creatures.Species sp, Creatures.Rarity r, float wobble, float seed)
            {
                var c = CreatureBody.Create(creatureMat, null, 0.27f * size, wobble, Creatures.Lobes(sp), seed, r);
                c.Look = cam.transform;
                made.Add(c.gameObject);
                return c;
            }

            // One coming out of the top-right square, half condensed, floating where the blob was.
            var rising = Make(Creatures.Species.Starfin, Creatures.Rarity.Rare, 0.15f, 40f);
            rising.transform.SetPositionAndRotation(patches[1].position + Vector3.up * 0.045f,
                Quaternion.LookRotation(patches[1].up, -patches[1].forward));
            rising.Dissolve = 0.45f;

            // The nest beside the right-hand column, one of each species, rising rarity.
            var kinds = new[]
            {
                (Creatures.Species.Trifoil, Creatures.Rarity.Common), (Creatures.Species.Clover, Creatures.Rarity.Uncommon),
                (Creatures.Species.Sixling, Creatures.Rarity.Rare), (Creatures.Species.Starfin, Creatures.Rarity.Elusive)
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                var c = Make(kinds[i].Item1, kinds[i].Item2, 0.12f + 0.03f * i, 100f + 37f * i);
                Vector3 pos = center + new Vector3(step + size * 0.5f + 0.06f, 0.002f, (1.5f - i) * 0.045f);
                Vector3 toCam = Vector3.ProjectOnPlane(new Vector3(0f, 0.42f, 0.18f) - pos, Vector3.up);
                c.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCam, Vector3.up));
                c.transform.localScale = Vector3.one * 0.55f;
            }

            void Shoot(Vector3 eye, Vector3 at, string name)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
                foreach (var c in UnityEngine.Object.FindObjectsByType<CreatureBody>(FindObjectsSortMode.None)) c.Refresh();
                Render(cam, rt);
                Save(rt, outDir + "/" + name + ".png");
            }

            Shoot(new Vector3(0f, 0.42f, 0.18f), center + new Vector3(0.04f, 0f, 0f), "seated-view");
            Shoot(patches[1].position + new Vector3(-0.08f, 0.16f, -0.14f), patches[1].position + Vector3.up * 0.05f, "close-up");
            rising.Dissolve = 0f;
            Shoot(patches[1].position + new Vector3(-0.08f, 0.16f, -0.14f), patches[1].position + Vector3.up * 0.05f, "close-up-solid");

            cam.targetTexture = null; rt.Release();
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            Debug.Log("[Cyclopea] Creature captures written to " + outDir);
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
