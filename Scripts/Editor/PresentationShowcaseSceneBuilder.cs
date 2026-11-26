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
    /// <c>kmax-human-anatomy/Scene/Main.unity</c>: 27" rig, stereo cameras on a dark slate background, the four-colour stylus beam, the
    /// stylus tip, the event system, the rig's Key, Fill and Rim lights, the dust, and the anatomy viewer and pen mapping (button 0 turns
    /// the view, button 1 resets it, button 2 pushed or pulled zooms; the mouse drag, R and the wheel do the same).
    ///
    /// <para>The interface has no buttons: only the shown model's title and one short line of description at the top left, fading with
    /// the model's pop, in the anatomy header style (Design.md 6.1, no Home button).</para>
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
        private const string RigPrefabPath = "Assets/Plugins/KmaxVxSDK/KMAX_Core/com.kmax.xr.core/Editor Resources/XRRig.prefab";
        private const string StylusTipMaterialPath = "Assets/Games/kmax-human-anatomy/Content/Materials/StylusTip.mat";
        private const string DustMaterialPath = "Assets/Games/kmax-human-anatomy/Content/Materials/LauncherDust.mat";
        private const string RigName = "XRRig";
        private const string DirectorName = "PresentationDirector";
        private const string CarouselName = "Carousel";
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
            KmaxRigBuilder.EnsureEventSystem();
            StylusTip tip = KmaxRigBuilder.EnsureTip(rig, 0.004f, false);
            KmaxRigBuilder.EnsureBeam(rig, StylusTipMaterialPath);
            KmaxRigBuilder.EnsureDiagnostics(tip);
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

        /// <summary>The 27" virtual screen, as in the anatomy scene. Applying it makes the SDK recompute the window size.</summary>
        private static void ConfigureScreen(GameObject rig) {
            SerializedObject serialized = new SerializedObject(rig.GetComponent<XRRig>());
            SerializedProperty screenType = serialized.FindProperty("screen.screenType");
            if (screenType == null) {
                Debug.LogWarning($"{LogPrefix}XRRig has no 'screen.screenType' field; the SDK changed and the screen size was not set.");
                return;
            }

            screenType.enumValueIndex = (int)VirtualScreen.ScreenType.Screen27;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The anatomy scene's three directional lights on the rig, no shadows, flat ambient and no skybox.</summary>
        private static void EnsureLighting(GameObject rig) {
            ConfigureLight(rig.transform, "Key Light", new Color(1f, 0.96f, 0.9f, 1f), 1.15f, Quaternion.Euler(38f, -28f, 0f));
            ConfigureLight(rig.transform, "Fill Light", new Color(0.7f, 0.8f, 1f, 1f), 0.35f, Quaternion.Euler(-12f, 42f, 0f));
            ConfigureLight(rig.transform, "Rim Light", new Color(0.6f, 0.75f, 1f, 1f), 0.7f, Quaternion.Euler(8f, 180f, 0f));

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.14f, 0.16f, 0.2f, 1f);
        }

        private static void ConfigureLight(Transform parent, string lightName, Color color, float intensity, Quaternion rotation) {
            GameObject host = KmaxRigBuilder.FindOrCreateChild(parent, lightName);
            Light light = host.GetComponent<Light>();
            if (light == null) {
                light = host.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
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

            ParticleLayerSpec far = new ParticleLayerSpec();
            far.name = "Particles_Far";
            far.localPosition = new Vector3(0f, 0f, 0.14f);
            far.shapeType = ParticleSystemShapeType.Box;
            far.shapeScale = new Vector3(0.8f, 0.46f, 0.12f);
            far.minSize = 0.0018f;
            far.maxSize = 0.0036f;
            far.minAlpha = 0.2f;
            far.maxAlpha = 0.5f;
            far.rate = 9f;
            far.maxParticles = 220;
            BuildParticleLayer(group.transform, far, material);

            ParticleLayerSpec middle = new ParticleLayerSpec();
            middle.name = "Particles_Beside";
            middle.localPosition = new Vector3(0f, 0f, -0.02f);
            middle.shapeType = ParticleSystemShapeType.Donut;
            middle.radius = 0.24f;
            middle.donutRadius = 0.05f;
            middle.shapeScale = new Vector3(1f, 0.55f, 1f);
            middle.minSize = 0.0012f;
            middle.maxSize = 0.0026f;
            middle.minAlpha = 0.25f;
            middle.maxAlpha = 0.6f;
            middle.rate = 6f;
            middle.maxParticles = 140;
            BuildParticleLayer(group.transform, middle, material);

            ParticleLayerSpec near = new ParticleLayerSpec();
            near.name = "Particles_Near";
            near.localPosition = new Vector3(0f, 0f, -0.07f);
            near.shapeType = ParticleSystemShapeType.Donut;
            near.radius = 0.22f;
            near.donutRadius = 0.03f;
            near.shapeScale = new Vector3(1f, 0.5f, 1f);
            near.minSize = 0.0008f;
            near.maxSize = 0.0016f;
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

            GameObject root = new GameObject(InterfaceName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            canvas.sortingOrder = SortingOrder;
            root.AddComponent<KmaxUIRaycaster>();
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
        /// The viewer of the anatomy scene: the rig orbits the model at 0.47 m, the dolly is reported and not applied to the camera (the
        /// view control zooms the model instead), and the pen's three buttons drive it through <see cref="StylusNavigation"/>.
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
            viewerObject.ApplyModifiedPropertiesWithoutUndo();

            StylusNavigation navigation = host.GetComponent<StylusNavigation>();
            if (navigation == null) {
                navigation = host.AddComponent<StylusNavigation>();
            }

            SerializedObject navigationObject = new SerializedObject(navigation);
            KmaxRigBuilder.SetReference(navigationObject, "flyController", viewer);
            KmaxRigBuilder.SetReference(navigationObject, "rigRoot", rig.transform);
            KmaxRigBuilder.SetBool(navigationObject, "enableDolly", true);
            navigationObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject controlObject = new SerializedObject(viewControl);
            KmaxRigBuilder.SetReference(controlObject, "viewer", viewer);
            KmaxRigBuilder.SetFloat(controlObject, "homeYaw", HomeYaw);
            KmaxRigBuilder.SetFloat(controlObject, "homePitch", HomePitch);
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
                targetObject.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"{LogPrefix}Copied {sourceItems.arraySize} models from {ItemsSourceScenePath}.");
                return true;
            } finally {
                EditorSceneManager.CloseScene(source, true);
                EditorSceneManager.SetActiveScene(target);
            }
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
