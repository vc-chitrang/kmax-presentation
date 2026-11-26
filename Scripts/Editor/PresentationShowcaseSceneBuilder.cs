using KmaxXR;

using TMPro;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using ViitorCloud.KmaxDisplay;
using ViitorCloud.KmaxDisplay.Editor;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Builds <c>Scenes/PresentationScene/PresentationShowcase.unity</c>: one model at a time in the centre of the screen, picked in a
    /// random order, for 8 seconds each, popping in and out with a linear 0.24 s DOTween scale, on the Kmax display standard of
    /// <c>kmax-human-anatomy/Scene/Main.unity</c>: 27" rig (the shared linked XRRig prefab), stereo cameras on a dark slate background,
    /// the rig's Key, Fill and Rim lights and the dust.
    ///
    /// <para>A presentation only: the pen is switched off and nothing reads touch, mouse, wheel or keys (<see cref="BlockInput"/>). The
    /// interface has no buttons and no raycaster: only the shown model's title and one short line of description at the top left,
    /// fading with the model's pop, in the anatomy header style (Design.md 6.1, no Home button).</para>
    ///
    /// <para>The models and their rotations are copied from the director of <c>Scenes/PresentationScene_v01.unity</c>. Every model is
    /// fitted by <see cref="PresentationDirector"/> alone; there is no per-model offset or scale.</para>
    ///
    /// <para>Like the anatomy builders it converges on the spec: each step finds what exists and re-applies it, so running it twice gives
    /// the same scene.</para>
    /// </summary>
    public static class PresentationShowcaseSceneBuilder {
        public const string ScenePath = "Assets/Games/kmax-presentation/Scenes/PresentationScene/PresentationShowcase.unity";

        private const string ItemsSourceScenePath = "Assets/Games/kmax-presentation/Scenes/PresentationScene_v01.unity";
        /// <summary>The 30 degree incline of the shoes (owner, 2026-11-26), set as a base rotation like the rings' lay-back.</summary>
        private const float FootwearTiltDegrees = 30.0f;
        /// <summary>How far the Solar System is inclined towards the viewer (owner, 2026-11-26: fix the angle; it was 38, then +55, which the owner had flipped to -55; finally 0, parallel to the ground).</summary>
        private const float SolarSystemTiltDegrees = 0.0f;
        private const float PendantTiltDegrees = -35.0f;
        private const string FashionModelsPath = "Assets/Games/kmax-presentation/Fashion/Models";
        private const string StudioReflectionPath = "Assets/Games/kmax-presentation/Rendering/ShowcaseStudioReflection.asset";
        private const string PostFxProfilePath = "Assets/Games/kmax-presentation/Rendering/PresentationPostFX.asset";
        private const string PostFxName = "PostFX";
        private const int StudioReflectionSize = 128;

        /// <summary>Width in metres of the 27" virtual window, the size the dust layers were first made for.</summary>
        private const float Window27Width = 0.5977f;
        private const string JewelleryModelsPath ="Assets/Games/kmax-presentation/Jewellery/Models";
        private const string RigPrefabPath ="Assets/Plugins/KmaxVxSDK/KMAX_Core/com.kmax.xr.core/Editor Resources/XRRig.prefab";
        private const string DustMaterialPath = "Assets/Games/kmax-human-anatomy/Content/Materials/LauncherDust.mat";
        private const string RigName = "XRRig";
        private const string DirectorName = "PresentationDirector";
        private const string CarouselName = "Carousel";
        private const string TuningAssetPath = "Assets/Games/kmax-presentation/Data/PresentationTuning.asset";
        private const string LegacyDustName = "BackdropDust";
        private const string ParticlesName = "BackdropParticles";
        private const string ViewerName = "Viewer";
        private const string InterfaceName = "Interface";
        private const string LogPrefix = "[Presentation Showcase] ";

        /// <summary>
        /// Distance from the eye to the model's centre before the first model is placed, as in the anatomy scene. The eye is 0.5 m from the
        /// glass. <see cref="PresentationViewControl"/> then sets it per model, inside the minimum and maximum below (0.12 m in front of the
        /// glass to 0.12 m behind it).
        /// </summary>
        private const float ViewerDistance = 0.47f;
        private const float ViewerDistanceMinimum = 0.38f;
        private const float ViewerDistanceMaximum = 0.62f;
        /// <summary>Home view angle of the retail showroom: level yaw, looking 14 degrees down on the model.</summary>
        /// <summary>Metres kept between a model's nearest point and the display's 13 cm pop-out limit.</summary>
        private const float NearestMargin = 0.01f;
        private const float HomeYaw = 0f;
        private const float HomePitch = 14f;
        /// <summary>The depth the director fits each model's size for (positive behind the glass). Kept as first built so no model's scale changes.</summary>
        private const float StageCentre = -0.03f;

        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const int SortingOrder = 100;
        private const float HeaderLeft = 32f;
        private const float HeaderTop = 22f;
        private const float TitleFontSize = 36f;
        private const float DescriptionFontSize = 18f;
        private const float DescriptionTop = 46f;

        /// <summary>Categories (the subtitle after its bullet) left out of the showcase. Owner, 2026-11-26: Chemistry is no longer shown.</summary>
        private static readonly string[] ExcludedCategories = new string[] { "Chemistry" };
        /// <summary>Models left out by title (owner, 2026-11-26: no Lamborghini).</summary>
        private static readonly string[] ExcludedTitles = new string[] { "Lamborghini" };

        private static readonly Color BackgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
        private static readonly Color AccentBright = new Color32(34, 211, 238, 255);

        [MenuItem("KMax/Presentation/Build Showcase Scene (one model at a time)")]
        public static void Build() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                Debug.LogWarning($"{LogPrefix}Build cancelled: the open scenes have unsaved changes.");
                return;
            }

            Scene scene = KmaxRigBuilder.EnsureScene(ScenePath);
            RemoveStockCameras();

            GameObject rig = EnsureRig();
            if (rig == null) {
                return;
            }

            ConfigureScreen(rig);
            KmaxRigBuilder.ConfigureStereoCameras(rig, BackgroundColor);
            BlockInput(rig);
            EnsureLighting(rig);
            EnsureParticleLayers(rig);

            Camera eventCamera = rig.GetComponentInChildren<Camera>(true);
            TMP_Text titleText;
            TMP_Text descriptionText;
            CanvasGroup headerGroup = EnsureInterface(eventCamera, ReadWindowSize(rig), out titleText, out descriptionText);

            PresentationDirector director = EnsureDirector(titleText, descriptionText, headerGroup);
            if (!CopyItems(director)) {
                return;
            }

            AddJewelleryItems(director);
            AddFashionItems(director);
            ApplyPcbMotion(director);

            PresentationViewControl viewControl = EnsureViewControl(director);
            EnsureViewer(rig, viewControl);

            EditorSceneManager.SetActiveScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{LogPrefix}Built {ScenePath}.");
        }

        /// <summary>A new scene from a template can carry a stock Main Camera; two sets of cameras and listeners would fight the rig.</summary>
        private static void RemoveStockCameras() {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                bool insideRig = cameras[i].GetComponentInParent<XRRig>() != null;
                if (!insideRig && cameras[i].gameObject.name == "Main Camera") {
                    Object.DestroyImmediate(cameras[i].gameObject);
                }
            }
        }

        /// <summary>
        /// The globally used rig: a linked instance of the shared SDK prefab, as in the launcher, the v01 presentation and the other
        /// experience scenes, so a change to that prefab reaches this scene too. What this builder sets on it (screen, cameras, lights,
        /// tip, beam) is kept as prefab overrides. A rig that is not a linked instance of that prefab is replaced.
        /// <see cref="KmaxRigBuilder.EnsureRig"/> is not used because its prefab path still points at the anatomy repository's own copy
        /// of the SDK, which is no longer there, and it unpacks the prefab.
        /// </summary>
        private static GameObject EnsureRig() {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (prefab == null) {
                Debug.LogError($"{LogPrefix}The Kmax rig prefab is missing at {RigPrefabPath}; the scene was not built.");
                return null;
            }

            XRRig existing = Object.FindFirstObjectByType<XRRig>();
            if (existing != null) {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject);
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(existing.gameObject) && source == prefab) {
                    existing.gameObject.name = RigName;
                    return existing.gameObject;
                }

                Debug.Log($"{LogPrefix}Replaced a rig that was not a linked instance of {RigPrefabPath}.");
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = RigName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return instance;
        }

        /// <summary>
        /// The virtual screen the scene is built on: 27", the standard of the human anatomy scene (owner, 2026-11-26: that scene is the base
        /// and standard). The SDK maps the real display onto this window (<c>PNClient</c>: screen scale = virtual width / real width), so
        /// a 15.6" scene is shown about 1.73 times larger and deeper on the same display. 11 of the 13 reference experiences are 15.6"
        /// (cars, engines, PCB, aircraft, Mona Lisa, solar system, retail); the anatomy scene and the eye scene are 27". The sizes in the
        /// tuning asset carry the 15.6" references over by that factor; their depth cannot, because the near clip and the comfort limit
        /// (13 cm) are fixed in scene units.
        /// </summary>
        private const VirtualScreen.ScreenType ScreenBaseline = VirtualScreen.ScreenType.Screen27;

        /// <summary>Applies <see cref="ScreenBaseline"/>. Applying it makes the SDK recompute the window size.</summary>
        private static void ConfigureScreen(GameObject rig) {
            SerializedObject serialized = new SerializedObject(rig.GetComponent<XRRig>());
            SerializedProperty screenType = serialized.FindProperty("screen.screenType");
            if (screenType == null) {
                Debug.LogWarning($"{LogPrefix}XRRig has no 'screen.screenType' field; the SDK changed and the screen size was not set.");
                return;
            }

            screenType.enumValueIndex = (int)ScreenBaseline;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Studio lighting for models that float in dark space (owner, 2026-11-26: proper lighting). Four directional lights on the rig, so
        /// they keep the same side lit: a warm key with soft shadows, a neutral fill, a silver rim and a soft overhead light, in the colours
        /// of the presentation's lighting notes (<c>docs/lighting.md</c>). A three-tone ambient replaces the flat grey, and a generated
        /// studio reflection map (an overhead softbox, a key panel, side strips and a rim panel) gives metal, glass and paint something to
        /// reflect; without it they reflect black space and look dull. No skybox, no SSAO (stereo comfort).
        /// </summary>
        private static void EnsureLighting(GameObject rig) {
            ConfigureLight(rig.transform, "Key Light", new Color(1.00f, 0.98f, 0.94f, 1f), 1.10f, Quaternion.Euler(38f, -28f, 0f), LightShadows.None);
            ConfigureLight(rig.transform, "Fill Light", new Color(0.85f, 0.88f, 0.92f, 1f), 0.70f, Quaternion.Euler(-12f, 42f, 0f), LightShadows.None);
            ConfigureLight(rig.transform, "Rim Light", new Color(0.88f, 0.92f, 0.98f, 1f), 0.80f, Quaternion.Euler(8f, 180f, 0f), LightShadows.None);
            ConfigureLight(rig.transform, "Top Light", new Color(1.00f, 0.99f, 0.96f, 1f), 0.60f, Quaternion.Euler(88f, 0f, 0f), LightShadows.None);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.70f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.47f, 0.54f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.26f, 0.31f, 1f);
            RenderSettings.ambientIntensity = 1.0f;

            Cubemap reflection = EnsureStudioReflection();
            if (reflection != null) {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = reflection;
                RenderSettings.reflectionIntensity = 1.0f;
                RenderSettings.reflectionBounces = 1;
            }

            EnsurePostProcessing(rig);
        }

        /// <summary>
        /// A studio environment for reflections, made once and kept (delete the asset to make it again): a dark slate gradient with an
        /// overhead softbox, a warm key panel in front on the left, a cool fill panel in front on the right, two side strips and a rim panel
        /// behind. The values are above 1 where a light is, so highlights read as light and not as a grey patch.
        /// </summary>
        private static Cubemap EnsureStudioReflection() {
            // Rebuilt on every scene build, so a change to the studio colours always reaches the scene.
            AssetDatabase.DeleteAsset(StudioReflectionPath);

            KmaxRigBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(StudioReflectionPath).Replace('\\', '/'));
            Cubemap cubemap = new Cubemap(StudioReflectionSize, TextureFormat.RGBAHalf, true);
            CubemapFace[] faces = new CubemapFace[] {
                CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ
            };
            for (int f = 0; f < faces.Length; f++) {
                Color[] pixels = new Color[StudioReflectionSize * StudioReflectionSize];
                for (int y = 0; y < StudioReflectionSize; y++) {
                    float v = ((y + 0.5f) / StudioReflectionSize) * 2f - 1f;
                    for (int x = 0; x < StudioReflectionSize; x++) {
                        float u = ((x + 0.5f) / StudioReflectionSize) * 2f - 1f;
                        pixels[y * StudioReflectionSize + x] = StudioColor(CubeDirection(faces[f], u, v).normalized);
                    }
                }

                cubemap.SetPixels(pixels, faces[f]);
            }

            cubemap.Apply(true, false);
            AssetDatabase.CreateAsset(cubemap, StudioReflectionPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix}Made the studio reflection map at {StudioReflectionPath}.");
            return cubemap;
        }

        private static Vector3 CubeDirection(CubemapFace face, float u, float v) {
            switch (face) {
                case CubemapFace.PositiveX:
                    return new Vector3(1f, v, -u);
                case CubemapFace.NegativeX:
                    return new Vector3(-1f, v, u);
                case CubemapFace.PositiveY:
                    return new Vector3(u, 1f, -v);
                case CubemapFace.NegativeY:
                    return new Vector3(u, -1f, v);
                case CubemapFace.PositiveZ:
                    return new Vector3(u, v, 1f);
                default:
                    return new Vector3(-u, v, -1f);
            }
        }

        private static Color StudioColor(Vector3 dir) {
            // Dark slate, a little brighter towards the top.
            float up = Mathf.Clamp01((dir.y + 0.4f) / 1.4f);
            Color colour = Color.Lerp(new Color(0.16f, 0.18f, 0.22f, 1f), new Color(0.55f, 0.60f, 0.70f, 1f), up);

            colour += StudioPanel(dir, new Vector3(0f, 1f, 0f), 40f, new Color(1.00f, 0.97f, 0.90f, 1f) * 3.2f);
            colour += StudioPanel(dir, new Vector3(-0.50f, 0.45f, -0.74f), 24f, new Color(1.00f, 0.96f, 0.88f, 1f) * 4.0f);
            colour += StudioPanel(dir, new Vector3(0.70f, 0.10f, -0.70f), 22f, new Color(0.85f, 0.90f, 1.00f, 1f) * 1.6f);
            colour += StudioPanel(dir, new Vector3(-1f, 0f, 0f), 16f, new Color(0.95f, 0.97f, 1.00f, 1f) * 2.0f);
            colour += StudioPanel(dir, new Vector3(1f, 0f, 0f), 16f, new Color(0.95f, 0.97f, 1.00f, 1f) * 2.0f);
            colour += StudioPanel(dir, new Vector3(0.30f, 0.30f, 0.90f), 26f, new Color(0.85f, 0.92f, 1.00f, 1f) * 1.6f);
            colour.a = 1f;
            return colour;
        }

        /// <summary>A soft-edged round light: full inside <paramref name="radiusDegrees"/>, fading over a few degrees beyond.</summary>
        private static Color StudioPanel(Vector3 dir, Vector3 centre, float radiusDegrees, Color emission) {
            float cosRadius = Mathf.Cos(radiusDegrees * Mathf.Deg2Rad);
            float t = Vector3.Dot(dir, centre.normalized);
            float w = Mathf.Clamp01((t - (cosRadius - 0.06f)) / 0.06f);
            w = w * w * (3f - 2f * w);
            return emission * w;
        }

        /// <summary>
        /// The post-processing the presentation was approved with (<c>Rendering/PresentationPostFX.asset</c>: ACES tonemapping, a mild
        /// bloom, a light vignette, no SSAO), as a global volume, and <see cref="StereoPostProcessing"/> on the rig, because the SDK makes
        /// the eye cameras at run time without the data URP needs to apply a volume to them.
        /// </summary>
        private static void EnsurePostProcessing(GameObject rig) {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFxProfilePath);
            if (profile == null) {
                Debug.LogWarning($"{LogPrefix}{PostFxProfilePath} is missing; the scene has no post-processing.");
                return;
            }

            GameObject host = KmaxRigBuilder.FindOrCreateRoot(PostFxName);
            Volume volume = host.GetComponent<Volume>();
            if (volume == null) {
                volume = host.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 1f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            StereoPostProcessing stereo = rig.GetComponent<StereoPostProcessing>();
            if (stereo == null) {
                stereo = rig.AddComponent<StereoPostProcessing>();
            }

            SerializedObject stereoObject = new SerializedObject(stereo);
            KmaxRigBuilder.SetReference(stereoObject, "cameraRoot", rig.transform);
            stereoObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLight(Transform parent, string lightName, Color color, float intensity, Quaternion rotation, LightShadows shadows) {
            GameObject host = KmaxRigBuilder.FindOrCreateChild(parent, lightName);
            Light light = host.GetComponent<Light>();
            if (light == null) {
                light = host.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows;
            if (shadows != LightShadows.None) {
                // The values of the presentation's key light (docs/lighting.md).
                light.shadowBias = 0.04f;
                light.shadowNormalBias = 0.35f;
            }

            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = rotation;
        }

        /// <summary>
        /// Three layers of the anatomy dust (same motes, colour and material) around the model, so the volume reads as deep: a far field
        /// behind the model, a ring at the model's depth that sits beside it and leaves the middle clear, and a sparse inner ring in front
        /// of the glass. They are children of the rig and simulate in its space, so they stay still on screen while the view orbits
        /// (Rules.md R-10). The near ring stays well inside the window, so no mote in front of the glass is cut by the screen's edge.
        /// The single dust box of the first build is removed.
        /// </summary>
        private static void EnsureParticleLayers(GameObject rig) {
            GameObject oldDust = GameObject.Find(LegacyDustName);
            if (oldDust != null && oldDust.transform.parent == null) {
                Object.DestroyImmediate(oldDust);
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            if (material == null) {
                Debug.LogError($"{LogPrefix}The dust material is missing at {DustMaterialPath}; the particles are not drawn.");
            }

            GameObject group = KmaxRigBuilder.FindOrCreateChild(rig.transform, ParticlesName);
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;

            // The layers were sized for the 27" window; they follow the window of the chosen baseline so they fill it the same way.
            float k = ReadWindowSize(rig).x / Window27Width;

            ParticleLayerSpec far = new ParticleLayerSpec();
            far.name = "Particles_Far";
            far.localPosition = new Vector3(0f, 0f, 0.14f);
            far.shapeType = ParticleSystemShapeType.Box;
            far.shapeScale = new Vector3(0.8f * k, 0.46f * k, 0.12f);
            far.minSize = 0.0018f * k;
            far.maxSize = 0.0036f * k;
            far.minAlpha = 0.2f;
            far.maxAlpha = 0.5f;
            far.rate = 9f;
            far.maxParticles = 220;
            BuildParticleLayer(group.transform, far, material);

            ParticleLayerSpec middle = new ParticleLayerSpec();
            middle.name = "Particles_Beside";
            middle.localPosition = new Vector3(0f, 0f, -0.02f);
            middle.shapeType = ParticleSystemShapeType.Donut;
            middle.radius = 0.24f * k;
            middle.donutRadius = 0.05f * k;
            middle.shapeScale = new Vector3(1f, 0.55f, 1f);
            middle.minSize = 0.0012f * k;
            middle.maxSize = 0.0026f * k;
            middle.minAlpha = 0.25f;
            middle.maxAlpha = 0.6f;
            middle.rate = 6f;
            middle.maxParticles = 140;
            BuildParticleLayer(group.transform, middle, material);

            ParticleLayerSpec near = new ParticleLayerSpec();
            near.name = "Particles_Near";
            near.localPosition = new Vector3(0f, 0f, -0.07f);
            near.shapeType = ParticleSystemShapeType.Donut;
            near.radius = 0.22f * k;
            near.donutRadius = 0.03f * k;
            near.shapeScale = new Vector3(1f, 0.5f, 1f);
            near.minSize = 0.0008f * k;
            near.maxSize = 0.0016f * k;
            near.minAlpha = 0.25f;
            near.maxAlpha = 0.55f;
            near.rate = 2.5f;
            near.maxParticles = 60;
            BuildParticleLayer(group.transform, near, material);
        }

        private sealed class ParticleLayerSpec {
            public string name;
            public Vector3 localPosition;
            public ParticleSystemShapeType shapeType;
            public Vector3 shapeScale = Vector3.one;
            public float radius;
            public float donutRadius;
            public float minSize;
            public float maxSize;
            public float minAlpha;
            public float maxAlpha;
            public float rate;
            public int maxParticles;
        }

        private static void BuildParticleLayer(Transform parent, ParticleLayerSpec spec, Material material) {
            GameObject host = KmaxRigBuilder.FindOrCreateChild(parent, spec.name);
            host.SetActive(true);
            host.transform.localPosition = spec.localPosition;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            ParticleSystem particles = host.GetComponent<ParticleSystem>();
            if (particles == null) {
                particles = host.AddComponent<ParticleSystem>();
            }

            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(16f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.002f, 0.006f);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.minSize, spec.maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.86f, 1f, spec.minAlpha), new Color(0.62f, 0.86f, 1f, spec.maxAlpha));
            main.gravityModifier = 0f;
            main.maxParticles = spec.maxParticles;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = spec.rate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = spec.shapeType;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = spec.shapeScale;
            if (spec.shapeType == ParticleSystemShapeType.Donut) {
                shape.radius = spec.radius;
                shape.donutRadius = spec.donutRadius;
                shape.radiusThickness = 1f;
                shape.arc = 360f;
            }

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.004f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            Gradient fade = new Gradient();
            fade.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sharedMaterial = material;
        }

        private static Vector2 ReadWindowSize(GameObject rig) {
            SerializedObject serialized = new SerializedObject(rig.GetComponent<XRRig>());
            serialized.Update();
            SerializedProperty width = serialized.FindProperty("screen.width");
            SerializedProperty height = serialized.FindProperty("screen.height");
            if (width == null || height == null || width.floatValue <= 0f) {
                Debug.LogError($"{LogPrefix}Could not read the virtual screen size from the rig; the interface will be mis-sized in the scene view.");
                return new Vector2(0.5977f, 0.3362f);
            }

            return new Vector2(width.floatValue, height.floatValue);
        }

        /// <summary>
        /// The world-space interface of the anatomy scene (screen plane, sorting order 100, Kmax raycaster, drawn over the model) holding
        /// only the header: the model's title and one short line of description at the top left. Nothing on it takes a press. Rebuilt from
        /// scratch each time.
        /// </summary>
        private static CanvasGroup EnsureInterface(Camera eventCamera, Vector2 windowSize, out TMP_Text titleText, out TMP_Text descriptionText) {
            GameObject existing = GameObject.Find(InterfaceName);
            if (existing != null) {
                Object.DestroyImmediate(existing);
            }

            GameObject root = new GameObject(InterfaceName, typeof(RectTransform), typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            canvas.sortingOrder = SortingOrder;
            root.AddComponent<UIScaler>();
            root.AddComponent<UiAlwaysOnTop>();

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(ReferenceWidth, ReferenceHeight);
            rootRect.localScale = Vector3.one * (windowSize.x / ReferenceWidth);

            GameObject header = new GameObject("Header", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.SetParent(rootRect, false);
            SetRect(headerRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(HeaderLeft, -HeaderTop), new Vector2(700f, 80f));
            CanvasGroup group = header.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            titleText = CreateText(headerRect, "Title", TitleFontSize, Color.white, FontStyles.Bold);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, DescriptionTop));

            descriptionText = CreateText(headerRect, "Description", DescriptionFontSize, AccentBright, FontStyles.Normal);
            SetRect(descriptionText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -DescriptionTop), new Vector2(0f, 28f));
            return group;
        }

        private static TMP_Text CreateText(RectTransform parent, string objectName, float fontSize, Color color, FontStyles style) {
            GameObject host = new GameObject(objectName, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            TextMeshProUGUI text = host.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta) {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        /// <summary>
        /// A presentation only (owner, 2026-11-26): nothing a visitor does may change what is shown. The pen object of the rig is switched
        /// off (no tracking, no beam, no buttons) and the event system, the stylus tip and the diagnostics of earlier builds are removed,
        /// so neither the pen nor a touch or click reaches anything. The viewer's own mouse, touch, wheel and key input is switched off in
        /// <see cref="EnsureViewer"/>. The head tracker stays, so the stereo view still follows the viewer's eyes.
        /// </summary>
        private static void BlockInput(GameObject rig) {
            RemoveRoot("EventSystem");
            RemoveRoot("Diagnostics");

            Transform tip = rig.transform.Find("StylusTip");
            if (tip != null) {
                Object.DestroyImmediate(tip.gameObject);
            }

            KmaxStylus stylus = rig.GetComponentInChildren<KmaxStylus>(true);
            if (stylus == null) {
                Debug.LogWarning($"{LogPrefix}The rig has no pen object to switch off.");
                return;
            }

            stylus.gameObject.SetActive(false);
        }

        /// <summary>
        /// The per-model pop-out choices. Created empty the first time and never overwritten, so a rebuild keeps everything tuned and
        /// saved in the inspector.
        /// </summary>
        private static PresentationTuning EnsureTuningAsset() {
            PresentationTuning existing = AssetDatabase.LoadAssetAtPath<PresentationTuning>(TuningAssetPath);
            if (existing != null) {
                return existing;
            }

            KmaxRigBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(TuningAssetPath).Replace('\\', '/'));
            PresentationTuning created = ScriptableObject.CreateInstance<PresentationTuning>();
            AssetDatabase.CreateAsset(created, TuningAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix}Created the tuning asset at {TuningAssetPath}.");
            return created;
        }

        private static void RemoveRoot(string objectName) {
            GameObject existing = GameObject.Find(objectName);
            if (existing != null && existing.transform.parent == null) {
                Object.DestroyImmediate(existing);
            }
        }

        /// <summary>
        /// The viewer of the anatomy scene, used here only to place the rig: it holds the home view (level, 14 degrees down) at the distance
        /// <see cref="PresentationViewControl"/> chooses per model. Its fly input is off and it has no reset key and no pen navigation, so
        /// mouse drag, touch, wheel and keys do nothing.
        /// </summary>
        private static void EnsureViewer(GameObject rig, PresentationViewControl viewControl) {
            GameObject host = KmaxRigBuilder.FindOrCreateRoot(ViewerName);
            ViewerFlyController viewer = host.GetComponent<ViewerFlyController>();
            if (viewer == null) {
                viewer = host.AddComponent<ViewerFlyController>();
            }

            SerializedObject viewerObject = new SerializedObject(viewer);
            KmaxRigBuilder.SetReference(viewerObject, "rigRoot", rig.transform);
            KmaxRigBuilder.SetReference(viewerObject, "resetHandlerSource", viewControl);
            KmaxRigBuilder.SetFloat(viewerObject, "defaultDistance", ViewerDistance);
            KmaxRigBuilder.SetFloat(viewerObject, "minDistance", ViewerDistanceMinimum);
            KmaxRigBuilder.SetFloat(viewerObject, "maxDistance", ViewerDistanceMaximum);
            KmaxRigBuilder.SetFloat(viewerObject, "homeYaw", HomeYaw);
            KmaxRigBuilder.SetFloat(viewerObject, "homePitch", HomePitch);
            KmaxRigBuilder.SetBool(viewerObject, "applyDollyToCamera", false);
            KmaxRigBuilder.SetBool(viewerObject, "enableFly", false);
            KmaxRigBuilder.SetInt(viewerObject, "resetKey", (int)KeyCode.None);
            viewerObject.ApplyModifiedPropertiesWithoutUndo();

            StylusNavigation navigation = host.GetComponent<StylusNavigation>();
            if (navigation != null) {
                Object.DestroyImmediate(navigation);
            }

            SerializedObject controlObject = new SerializedObject(viewControl);
            KmaxRigBuilder.SetReference(controlObject, "viewer", viewer);
            KmaxRigBuilder.SetFloat(controlObject, "homeYaw", HomeYaw);
            KmaxRigBuilder.SetFloat(controlObject, "homePitch", HomePitch);
            // The nearest point of a model comes out to 12 cm (the comfort limit is 13), as in the reference experiences (cars 12.4 to 13.4).
            KmaxRigBuilder.SetFloat(controlObject, "nearestMargin", NearestMargin);
            controlObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The zoom and reset of the pen, and the comfort depth keeper, on the director's object: the keeper moves that object along the
        /// view axis, and the zoom scales the carousel under it, about the model's centre.
        /// </summary>
        private static PresentationViewControl EnsureViewControl(PresentationDirector director) {
            GameObject host = director.gameObject;
            ComfortDepthKeeper keeper = host.GetComponent<ComfortDepthKeeper>();
            if (keeper == null) {
                keeper = host.AddComponent<ComfortDepthKeeper>();
            }

            PresentationViewControl control = host.GetComponent<PresentationViewControl>();
            if (control == null) {
                control = host.AddComponent<PresentationViewControl>();
            }

            SerializedObject controlObject = new SerializedObject(control);
            KmaxRigBuilder.SetReference(controlObject, "director", director);
            KmaxRigBuilder.SetReference(controlObject, "content", host.transform.Find(CarouselName));
            KmaxRigBuilder.SetReference(controlObject, "depthKeeper", keeper);
            KmaxRigBuilder.SetReference(controlObject, "tuning", EnsureTuningAsset());
            controlObject.ApplyModifiedPropertiesWithoutUndo();
            return control;
        }

        /// <summary>
        /// The director and its carousel root. One model at a time, random order, 8 s each, linear 0.24 s pop in and out are the director's;
        /// this sets it for this scene: the stage at the origin the viewer orbits, models fitted inside the stereo comfort volume, no browse
        /// buttons, the header as its panel and the anatomy lights left as built.
        /// </summary>
        private static PresentationDirector EnsureDirector(TMP_Text titleText, TMP_Text descriptionText, CanvasGroup headerGroup) {
            GameObject host = KmaxRigBuilder.FindOrCreateRoot(DirectorName);
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            host.transform.localScale = Vector3.one;

            PresentationDirector director = host.GetComponent<PresentationDirector>();
            if (director == null) {
                director = host.AddComponent<PresentationDirector>();
            }

            GameObject carousel = KmaxRigBuilder.FindOrCreateChild(host.transform, CarouselName);
            carousel.transform.localPosition = Vector3.zero;
            carousel.transform.localRotation = Quaternion.identity;
            carousel.transform.localScale = Vector3.one;

            SerializedObject directorObject = new SerializedObject(director);
            KmaxRigBuilder.SetReference(directorObject, "carouselRoot", carousel.transform);
            KmaxRigBuilder.SetReference(directorObject, "titleText", titleText);
            KmaxRigBuilder.SetReference(directorObject, "subtitleText", null);
            KmaxRigBuilder.SetReference(directorObject, "descriptionText", descriptionText);
            KmaxRigBuilder.SetReference(directorObject, "panelGroup", headerGroup);
            KmaxRigBuilder.SetReference(directorObject, "modelPlacement", null);
            KmaxRigBuilder.SetReference(directorObject, "shadowFloor", null);
            KmaxRigBuilder.SetReference(directorObject, "fitCamera", null);
            KmaxRigBuilder.SetBool(directorObject, "showFloorShadow", false);
            KmaxRigBuilder.SetBool(directorObject, "stereoFit", true);
            KmaxRigBuilder.SetFloat(directorObject, "stageCentre", StageCentre);
            KmaxRigBuilder.SetBool(directorObject, "anchorStageToOrigin", true);
            KmaxRigBuilder.SetReference(directorObject, "tuning", EnsureTuningAsset());
            KmaxRigBuilder.SetBool(directorObject, "autoAdvance", true);
            KmaxRigBuilder.SetBool(directorObject, "debugBrowse", false);
            KmaxRigBuilder.SetBool(directorObject, "perItemLighting", false);
            directorObject.ApplyModifiedPropertiesWithoutUndo();
            return director;
        }

        /// <summary>
        /// Copies the item list (prefabs, rotations and per-model tuning) from the director of the v01 presentation scene, opened alongside
        /// and closed again without saving.
        /// </summary>
        private static bool CopyItems(PresentationDirector director) {
            Scene target = director.gameObject.scene;
            Scene source = EditorSceneManager.OpenScene(ItemsSourceScenePath, OpenSceneMode.Additive);
            try {
                PresentationDirector sourceDirector = FindDirector(source);
                if (sourceDirector == null) {
                    Debug.LogError($"{LogPrefix}No {nameof(PresentationDirector)} in {ItemsSourceScenePath}; the models were not copied and the scene was not saved.");
                    return false;
                }

                SerializedObject sourceObject = new SerializedObject(sourceDirector);
                SerializedProperty sourceItems = sourceObject.FindProperty("items");
                if (sourceItems == null || sourceItems.arraySize == 0) {
                    Debug.LogError($"{LogPrefix}The director in {ItemsSourceScenePath} has no items; the scene was not saved.");
                    return false;
                }

                SerializedObject targetObject = new SerializedObject(director);
                targetObject.CopyFromSerializedProperty(sourceItems);
                int removed = RemoveExcludedCategories(targetObject.FindProperty("items"));
                int furnitureRemoved = KeepOneFurniturePerCategory(targetObject.FindProperty("items"));
                targetObject.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"{LogPrefix}Copied {sourceItems.arraySize - removed - furnitureRemoved} models from {ItemsSourceScenePath} ({removed} left out: {string.Join(", ", ExcludedCategories)}; {furnitureRemoved} extra furniture models left out, one kept per category).");
                return true;
            } finally {
                EditorSceneManager.CloseScene(source, true);
                EditorSceneManager.SetActiveScene(target);
            }
        }

        /// <summary>
        /// Removes the items of a category the showcase no longer shows (owner, 2026-11-26: no Chemistry). The category is the part of an
        /// item's subtitle after its bullet, as in "Education  •  Chemistry". Returns how many items were removed.
        /// </summary>
        private static int RemoveExcludedCategories(SerializedProperty items) {
            int removed = 0;
            for (int i = items.arraySize - 1; i >= 0; i--) {
                string subtitle = items.GetArrayElementAtIndex(i).FindPropertyRelative("subtitle").stringValue;
                string title = items.GetArrayElementAtIndex(i).FindPropertyRelative("title").stringValue;
                if (IsExcludedCategory(subtitle) || IsExcludedTitle(title)) {
                    items.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Adds the jewellery models (owner, 2026-11-26: the Cuenect jewellery pack, copied into <see cref="JewelleryModelsPath"/>) after the
        /// copied items: one item per .glb, titled by kind and number (Ring 1 to 5, Earrings 1 to 4, Necklace 1 to 3, Bracelet 1), group
        /// Jewellery, shown on the turntable and sized by the director's fit like every other model. The text is kept neutral: nothing is
        /// said about a piece that the pack does not say. A file that is not one of those kinds is skipped with a warning.
        /// </summary>
        private static int AddJewelleryItems(PresentationDirector director) {
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new string[] { JewelleryModelsPath });
            System.Collections.Generic.List<string> paths = new System.Collections.Generic.List<string>();
            for (int i = 0; i < guids.Length; i++) {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase)) {
                    paths.Add(path);
                }
            }

            paths.Sort(System.StringComparer.Ordinal);
            if (paths.Count == 0) {
                Debug.LogWarning($"{LogPrefix}No jewellery models found in {JewelleryModelsPath}; none added.");
                return 0;
            }

            SerializedObject directorObject = new SerializedObject(director);
            SerializedProperty items = directorObject.FindProperty("items");
            System.Collections.Generic.Dictionary<string, int> counts = new System.Collections.Generic.Dictionary<string, int>();
            int added = 0;
            for (int i = 0; i < paths.Count; i++) {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(paths[i]);
                string kind = JewelleryKind(fileName);
                if (kind == null) {
                    Debug.LogWarning($"{LogPrefix}'{fileName}' is not a ring, earring, necklace or bracelet; skipped.");
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null) {
                    Debug.LogWarning($"{LogPrefix}'{paths[i]}' did not import as a model; skipped.");
                    continue;
                }

                int number = counts.ContainsKey(kind) ? counts[kind] + 1 : 1;
                counts[kind] = number;

                items.InsertArrayElementAtIndex(items.arraySize);
                SerializedProperty item = items.GetArrayElementAtIndex(items.arraySize - 1);
                item.FindPropertyRelative("title").stringValue = JewelleryTitle(fileName, kind, number);
                item.FindPropertyRelative("subtitle").stringValue = "Jewellery  •  " + kind;
                item.FindPropertyRelative("description").stringValue = "A piece from the jewellery collection.";
                item.FindPropertyRelative("shortDescription").stringValue = "From the jewellery collection";
                item.FindPropertyRelative("group").stringValue = "Jewellery";
                item.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                item.FindPropertyRelative("show").enumValueIndex = (int)PresentationShow.Turntable;
                item.FindPropertyRelative("fitSize").floatValue = 1.0f;
                item.FindPropertyRelative("baseEuler").vector3Value = JewelleryEuler(fileName);
                item.FindPropertyRelative("tiltDegrees").floatValue = 8.0f;
                item.FindPropertyRelative("spinDegreesPerSecond").floatValue = 26.0f;
                item.FindPropertyRelative("sway").boolValue = false;
                item.FindPropertyRelative("swayDegrees").floatValue = 32.0f;
                item.FindPropertyRelative("pulse").boolValue = false;
                item.FindPropertyRelative("anatomyTopic").stringValue = string.Empty;
                item.FindPropertyRelative("scale").floatValue = 1.0f;
                added++;
            }

            directorObject.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"{LogPrefix}Added {added} jewellery models from {JewelleryModelsPath}.");
            return added;
        }

        /// <summary>
        /// Adds the four models the owner supplied on 2026-11-26 (<see cref="FashionModelsPath"/>: three pairs of shoes and a pendant),
        /// shown on the turntable and sized by the director's fit like every other model. Titles are unique fancy names and the text says
        /// nothing about a model that the file does not say. A model that is missing is skipped with a warning.
        /// </summary>
        private static int AddFashionItems(PresentationDirector director) {
            string[,] models = new string[,] {
                { "Women_HighHeels", "Viviana High Heels", "Fashion  •  Footwear", "A pair of high-heeled shoes.", "Footwear", "Fashion" },
                { "female_shoes", "Giselle Shoes", "Fashion  •  Footwear", "A pair of women's shoes.", "Footwear", "Fashion" },
                { "male_shoes", "Lorenzo Shoes", "Fashion  •  Footwear", "A pair of men's shoes.", "Footwear", "Fashion" },
                { "pendant", "Eloise Pendant", "Jewellery  •  Pendant", "A pendant from the jewellery collection.", "From the jewellery collection", "Jewellery" }
            };

            SerializedObject directorObject = new SerializedObject(director);
            SerializedProperty items = directorObject.FindProperty("items");
            int added = 0;
            for (int i = 0; i < models.GetLength(0); i++) {
                string path = FashionModelsPath + "/" + models[i, 0] + ".fbx";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) {
                    Debug.LogWarning($"{LogPrefix}Model {path} was not found; it was not added.");
                    continue;
                }

                items.InsertArrayElementAtIndex(items.arraySize);
                SerializedProperty item = items.GetArrayElementAtIndex(items.arraySize - 1);
                item.FindPropertyRelative("title").stringValue = models[i, 1];
                item.FindPropertyRelative("subtitle").stringValue = models[i, 2];
                item.FindPropertyRelative("description").stringValue = models[i, 3];
                item.FindPropertyRelative("shortDescription").stringValue = models[i, 4];
                item.FindPropertyRelative("group").stringValue = models[i, 5];
                item.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                item.FindPropertyRelative("show").enumValueIndex = (int)PresentationShow.Turntable;
                item.FindPropertyRelative("fitSize").floatValue = 1.0f;
                item.FindPropertyRelative("baseEuler").vector3Value = FashionEuler(models[i, 0]);
                item.FindPropertyRelative("tiltDegrees").floatValue = 8.0f;
                item.FindPropertyRelative("spinDegreesPerSecond").floatValue = 26.0f;
                item.FindPropertyRelative("sway").boolValue = false;
                item.FindPropertyRelative("swayDegrees").floatValue = 32.0f;
                item.FindPropertyRelative("pulse").boolValue = false;
                item.FindPropertyRelative("anatomyTopic").stringValue = string.Empty;
                item.FindPropertyRelative("scale").floatValue = 1.0f;
                added++;
            }

            directorObject.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"{LogPrefix}Added {added} models from {FashionModelsPath}.");
            return added;
        }

        /// <summary>The rotation that sets a supplied model upright (Unity order: Z, then X, then Y); set after looking at each model in the Game view.</summary>
        private static Vector3 FashionEuler(string fileName) {
            switch (fileName.ToLowerInvariant()) {
                case "pendant":
                    // The same lay-back as the rings (owner, 2026-11-26), so the loop never turns edge-on.
                    return new Vector3(PendantTiltDegrees, 0.0f, 0.0f);
                default:
                    // The shoes: inclined 30 degrees as a base rotation, like the rings.
                    return new Vector3(FootwearTiltDegrees, 0.0f, 0.0f);
            }
        }

        /// <summary>
        /// The name a piece is shown under (owner, 2026-11-26: a fancy name for each). Names are unique, because the tuning asset finds a
        /// model by its title, and say nothing about the design. A file with no name here is shown as its kind and number.
        /// </summary>
        private static string JewelleryTitle(string fileName, string kind, int number) {
            switch (fileName.ToLowerInvariant()) {
                case "ring_00":
                    return "Aurelia Ring";
                case "ring_01":
                    return "Seraphine Ring";
                case "ring_02":
                    return "Valentina Ring";
                case "ring_04":
                    return "Duchess Ring";
                case "ring_05":
                    return "Empress Ring";
                case "earring_00":
                    return "Celeste Earrings";
                case "earring_01":
                    return "Lumiere Earrings";
                case "earring_02":
                    return "Opaline Earrings";
                case "earring_03":
                    return "Isolde Earrings";
                case "necklace_01":
                    return "Cleopatra Necklace";
                case "necklace_02":
                    return "Isadora Necklace";
                case "necklace_03":
                    return "Marchesa Necklace";
                case "braclate_00":
                case "bracelet_00":
                    return "Aphrodite Bracelet";
                default:
                    return kind + " " + number;
            }
        }

        /// <summary>
        /// The rotation, in degrees, that sets a piece the way it is shown (Unity order: Z, then X, then Y). The turntable turns a piece
        /// about the vertical axis. Three rings (Ring_00, Ring_01, Ring_02: Aurelia, Seraphine, Valentina; owner, 2026-11-26) stand
        /// in the plane that faces the viewer, so they were edge-on for a large part of each turn and hid their stones; the owner then
        /// asked for the same on all the rings, so Ring_04 and Ring_05 (Duchess, Empress) are set too. The first three are laid back
        /// 35 degrees (negative X: the face tilts up towards the viewer), and Ring_01, which the file has upside down with its stone at
        /// the bottom, is turned 180 degrees about Z first. Zero for every other piece.
        /// </summary>
        private static Vector3 JewelleryEuler(string fileName) {
            switch (fileName.ToLowerInvariant()) {
                case "ring_00":
                    return new Vector3(-35.0f, 0.0f, 0.0f);
                case "ring_01":
                    return new Vector3(-35.0f, 0.0f, 180.0f);
                case "ring_02":
                    return new Vector3(-35.0f, 0.0f, 0.0f);
                case "ring_04":
                case "ring_05":
                    // These two lie flat (the finger hole points straight up, the stones on the far side). Tipping them 55 degrees towards
                    // the viewer gives the same laid-back view as the other rings: the hole 35 degrees above the horizontal.
                    return new Vector3(-55.0f, 0.0f, 0.0f);
                case "braclate_00":
                case "bracelet_00":
                case "earring_03":
                    // Owner, 2026-11-26: the same lay-back as the rings (the face tilts 35 degrees up towards the viewer), so these pieces
                    // never turn edge-on on the turntable: Aphrodite Bracelet and Isolde Earrings.
                    return new Vector3(-35.0f, 0.0f, 0.0f);
                case "earring_02":
                case "necklace_03":
                    // Opaline Earrings and Marchesa Necklace: the owner asked for the opposite incline of the others (+35, the face tips
                    // 35 degrees down).
                    return new Vector3(35.0f, 0.0f, 0.0f);
                default:
                    return Vector3.zero;
            }
        }

        /// <summary>The kind a pack file name stands for ("EarRing_02" is Earrings; the pack spells bracelet "Braclate"), or null.</summary>
        private static string JewelleryKind(string fileName) {
            if (fileName.StartsWith("EarRing", System.StringComparison.OrdinalIgnoreCase)) {
                return "Earrings";
            }
            if (fileName.StartsWith("Necklace", System.StringComparison.OrdinalIgnoreCase)) {
                return "Necklace";
            }
            if (fileName.StartsWith("Bracelet", System.StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("Braclate", System.StringComparison.OrdinalIgnoreCase)) {
                return "Bracelet";
            }
            if (fileName.StartsWith("Ring", System.StringComparison.OrdinalIgnoreCase)) {
                return "Ring";
            }

            return null;
        }

        /// <summary>
        /// Owner, 2026-11-26: one model per furniture category (one armchair, one bed, one dining table, one coffee table, one TV unit, one
        /// wardrobe). A furniture item is one of group Retail; its category is its title without the first word ("Oslo Armchair" is
        /// "Armchair", "Halo Coffee Table" is "Coffee Table"). The first item of each category in the list is kept. Returns how many
        /// items were removed.
        /// </summary>
        private static int KeepOneFurniturePerCategory(SerializedProperty items) {
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
            System.Collections.Generic.List<int> drop = new System.Collections.Generic.List<int>();
            for (int i = 0; i < items.arraySize; i++) {
                SerializedProperty item = items.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("group").stringValue != "Retail") {
                    continue;
                }

                string title = item.FindPropertyRelative("title").stringValue;
                int space = title.IndexOf(' ');
                string category = space >= 0 ? title.Substring(space + 1) : title;
                if (!seen.Add(category)) {
                    drop.Add(i);
                }
            }

            for (int i = drop.Count - 1; i >= 0; i--) {
                items.DeleteArrayElementAtIndex(drop[i]);
            }

            return drop.Count;
        }

        /// <summary>
        /// Gives the PCB board the same motion as the Oslo Armchair (owner, 2026-11-26): a slight tilt, a slow spin and a side-to-side sway.
        /// The values are read from the armchair item, so the two always match.
        /// </summary>
        private static void ApplyPcbMotion(PresentationDirector director) {
            SerializedObject directorObject = new SerializedObject(director);
            SerializedProperty items = directorObject.FindProperty("items");
            SerializedProperty armchair = FindItemByTitle(items, "Oslo Armchair");
            SerializedProperty pcb = FindItemByTitle(items, "LM358");
            if (armchair == null || pcb == null) {
                Debug.LogWarning($"{LogPrefix}Could not give the PCB board the armchair's motion: the armchair or the PCB item is missing.");
                return;
            }

            string[] fields = new string[] { "tiltDegrees", "spinDegreesPerSecond", "swayDegrees" };
            for (int i = 0; i < fields.Length; i++) {
                pcb.FindPropertyRelative(fields[i]).floatValue = armchair.FindPropertyRelative(fields[i]).floatValue;
            }

            pcb.FindPropertyRelative("sway").boolValue = armchair.FindPropertyRelative("sway").boolValue;

            SerializedProperty solar = FindItemByTitle(items, "Solar System");
            if (solar != null) {
                solar.FindPropertyRelative("tiltDegrees").floatValue = SolarSystemTiltDegrees;
            }

            directorObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty FindItemByTitle(SerializedProperty items, string titlePrefix) {
            for (int i = 0; i < items.arraySize; i++) {
                SerializedProperty item = items.GetArrayElementAtIndex(i);
                string title = item.FindPropertyRelative("title").stringValue;
                if (title.StartsWith(titlePrefix, System.StringComparison.OrdinalIgnoreCase)) {
                    return item;
                }
            }

            return null;
        }

        private static bool IsExcludedTitle(string title) {
            for (int i = 0; i < ExcludedTitles.Length; i++) {
                if (!string.IsNullOrEmpty(title) && title.StartsWith(ExcludedTitles[i], System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExcludedCategory(string subtitle) {
            if (string.IsNullOrEmpty(subtitle)) {
                return false;
            }

            int bullet = subtitle.LastIndexOf('•');
            string category = bullet >= 0 ? subtitle.Substring(bullet + 1).Trim() : subtitle.Trim();
            for (int i = 0; i < ExcludedCategories.Length; i++) {
                if (string.Equals(category, ExcludedCategories[i], System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static PresentationDirector FindDirector(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                PresentationDirector found = roots[i].GetComponentInChildren<PresentationDirector>(true);
                if (found != null) {
                    return found;
                }
            }

            return null;
        }
    }
}
