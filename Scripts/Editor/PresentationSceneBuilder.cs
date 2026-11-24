using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ViitorCloud.KmaxDisplayExample;
using ViitorCloud.KmaxSolarSystem.Editor;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Builds PresentationScene: a hands-off random carousel of every developed model (Mission Control and Explore Parts excluded),
    /// one bottom-centre information panel, no buttons, no EventSystem, no way out (owner spec 2026-11-24). Lighting, post-processing and
    /// camera follow the experience scenes; the aircraft is extracted from the AircraftFlight experience so its wind and propeller run.
    /// </summary>
    public static class PresentationSceneBuilder {
        public const string ScenePath = "Assets/Games/kmax-presentation/Scenes/PresentationScene.unity";
        private const string SourceScenePath = "Assets/Games/kmax-education/Scenes/SolarSystem.unity";
        private const string AircraftScenePath = "Assets/Games/kmax-education/Scenes/AircraftFlight.unity";
        private const string AircraftPrefabPath = "Assets/Games/kmax-presentation/Prefabs/PresentationAircraft.prefab";

        private const string AnatomyRoot = "Assets/Games/kmax-human-anatomy/Generated/Resources/Anatomy/";
        private const string PcbRoot = "Assets/Games/KMaxPCBAssembly/Prefabs/Exhibits/";
        private const string RetailRoot = "Assets/Games/kmax-retail-products/Prefabs/";
        private const string EducationRoot = "Assets/Games/kmax-education/Prefabs/";

                private const string ThemeBackdropTexturePath = "Assets/Games/kmax-presentation/Backdrop/PresentationBackdrop_Theme.png";
        private const string ThemeBackdropMaterialPath = "Assets/Games/kmax-presentation/Backdrop/PresentationBackdrop.mat";
        private static readonly Color Backdrop = new Color(0.143f, 0.167f, 0.210f, 1.0f);

        private sealed class Entry {
            public string group;
            public string title;
            public string subtitle;
            public string description;
            public string prefabPath;
            public PresentationShow show = PresentationShow.Turntable;
            public float fit = 0.83f;
            public float tilt = 10.0f;
            public float spin = 26.0f;
            public bool sway;
            public float swayDeg = 32.0f;
            public bool pulse;
            public string anatomy = string.Empty;
            public Vector3 euler = Vector3.zero;
        }

        private static List<Entry> BuildCatalog() {
            List<Entry> list = new List<Entry>();

            list.Add(new Entry { group = "Biology", title = "Human Body", subtitle = "Biology  •  Whole-body atlas", description = "A full human body built from separate systems, with the organs glowing in turn and the layers drawn apart.", prefabPath = AnatomyRoot + "body.prefab", anatomy = "body", show = PresentationShow.Explode, fit = 0.78f, tilt = 3.0f });
            list.Add(new Entry { group = "Biology", title = "Heart", subtitle = "Biology  •  The body's pump", description = "The heart pushes blood around the body, beating about once every second at rest.", prefabPath = AnatomyRoot + "heart.prefab", anatomy = "heart", fit = 0.64f, tilt = 6.0f });
            list.Add(new Entry { group = "Biology", title = "Brain", subtitle = "Biology  •  Command centre", description = "The brain controls movement, memory and thought through billions of connected nerve cells.", prefabPath = AnatomyRoot + "brain.prefab", anatomy = "brain", pulse = true, fit = 0.64f, tilt = 10.0f });
            list.Add(new Entry { group = "Biology", title = "Ear", subtitle = "Biology  •  Hearing and balance", description = "The ear turns sound waves into nerve signals and helps the body keep its balance.", prefabPath = AnatomyRoot + "ear.prefab", anatomy = "ear", fit = 0.64f, tilt = 6.0f });
            list.Add(new Entry { group = "Biology", title = "Eye", subtitle = "Biology  •  How we see", description = "The eye focuses light onto the retina, which sends the picture to the brain.", prefabPath = AnatomyRoot + "eye.prefab", anatomy = "eye", fit = 0.64f, tilt = 4.0f });
            list.Add(new Entry { group = "Biology", title = "Breathing", subtitle = "Biology  •  Lungs and airways", description = "Air travels down the windpipe into the lungs, where oxygen passes into the blood.", prefabPath = AnatomyRoot + "breathing.prefab", anatomy = "breathing", fit = 0.78f, tilt = 4.0f });
            list.Add(new Entry { group = "Biology", title = "Skull", subtitle = "Biology  •  Head protection", description = "The skull shields the brain and gives the face its shape; the jaw opens and closes on its hinge.", prefabPath = AnatomyRoot + "skull.prefab", anatomy = "skull", fit = 0.64f, tilt = 6.0f });
            list.Add(new Entry { group = "Biology", title = "Organs", subtitle = "Biology  •  Inside the torso", description = "The major organs of the torso, shown in their natural position and then drawn apart.", prefabPath = AnatomyRoot + "organs.prefab", anatomy = "organs", show = PresentationShow.Explode, fit = 0.74f, tilt = 6.0f });
            list.Add(new Entry { group = "Biology", title = "Body Scan", subtitle = "Biology  •  Layer by layer", description = "A scan-style view of the body that peels back one layer after another.", prefabPath = AnatomyRoot + "scan.prefab", anatomy = "scan", show = PresentationShow.Explode, fit = 0.78f, tilt = 3.0f });

            list.Add(new Entry { group = "Engineering", title = "Inline-Four Engine", subtitle = "Engineering  •  Running engine", description = "Four cylinders fire in turn, driving the crankshaft that powers the car.", prefabPath = "Assets/Games/kmax-display-example/CarEngineAnimated - i4/Prefabs/Enginei4.prefab", show = PresentationShow.EngineRun, fit = 0.64f, tilt = 14.0f, spin = 22.0f });
            list.Add(new Entry { group = "Engineering", title = "Pontiac V8 Engine", subtitle = "Engineering  •  Classic American V8", description = "A classic V8 engine, shown assembled and then pulled apart part by part.", prefabPath = "Assets/Games/kmax-automobile-engine/Prefabs/PontiacEnginePreview.prefab", show = PresentationShow.Explode, fit = 0.64f, tilt = 14.0f, spin = 22.0f });

            list.Add(new Entry { group = "Automobile", title = "Volvo S90", subtitle = "Automobile  •  Luxury saloon", description = "A Scandinavian luxury saloon, modelled in detail inside and out.", prefabPath = "Assets/Games/kmax-display-example/Model/VOLVO/Volvo S90.fbx", fit = 0.64f, tilt = 0.0f, spin = 20.0f });
            list.Add(new Entry { group = "Automobile", title = "Volvo C40 Recharge", subtitle = "Automobile  •  Electric crossover", description = "An all-electric compact crossover with a sloping roofline.", prefabPath = "Assets/Games/kmax-display-example/Model/VolvoC40/Prefabs/VolvoC40Preview.prefab", fit = 0.64f, tilt = 0.0f, spin = 20.0f });
            list.Add(new Entry { group = "Automobile", title = "Ford Mustang GT", subtitle = "Automobile  •  American muscle", description = "The Mustang GT, a V8 coupe and long-standing American performance icon.", prefabPath = "Assets/Games/kmax-display-example/Model/FordMustangGT/Prefabs/FordMustangGTPreview.prefab", fit = 0.64f, tilt = 0.0f, spin = 20.0f });

            list.Add(new Entry { group = "Electronics", title = "LM358 Fader", subtitle = "Electronics  •  Smooth dimming", description = "An LM358 op-amp circuit that fades an LED up and down smoothly.", prefabPath = PcbRoot + "Exhibit_LM358Fader.prefab", euler = new Vector3(45.0f, 180.0f, 0.0f), fit = 0.64f, tilt = 0.0f, spin = 0.0f });

            string[] armchairs = new string[] { "02", "03", "04", "05", "06" };
            for (int i = 0; i < armchairs.Length; i++) {
                list.Add(new Entry { group = "Retail", title = "Armchair " + armchairs[i], subtitle = "Retail  •  Seating", description = "An armchair from the range, turned slowly so every angle can be seen.", prefabPath = RetailRoot + "Armchair_" + armchairs[i] + ".prefab", fit = 0.64f, tilt = -20.0f, sway = true, swayDeg = 38.0f });
            }
            string[] beds = new string[] { "02", "03", "05", "06" };
            for (int i = 0; i < beds.Length; i++) {
                list.Add(new Entry { group = "Retail", title = "Bed " + beds[i], subtitle = "Retail  •  Bedroom", description = "A bed from the bedroom range, turned slowly so every side can be seen.", prefabPath = RetailRoot + "Bed_" + beds[i] + ".prefab", fit = 0.64f, tilt = -20.0f, sway = true, swayDeg = 38.0f });
            }

            string[] diningTables = new string[] { "02", "03", "04", "06" };
            for (int i = 0; i < diningTables.Length; i++) {
                list.Add(new Entry { group = "Retail", title = "Dining Table " + diningTables[i], subtitle = "Retail  •  Dining", description = "A dining table from the range, turned slowly so every side can be seen.", prefabPath = RetailRoot + "Dining_table_" + diningTables[i] + ".prefab", fit = 0.55f, tilt = -20.0f });
            }
            string[] coffeeTables = new string[] { "03", "17", "18", "19" };
            for (int i = 0; i < coffeeTables.Length; i++) {
                list.Add(new Entry { group = "Retail", title = "Coffee Table " + coffeeTables[i], subtitle = "Retail  •  Living room", description = "A coffee table from the range, turned slowly so every side can be seen.", prefabPath = RetailRoot + "Coffee_table_" + coffeeTables[i] + ".prefab", fit = 0.51f, tilt = -20.0f });
            }
            string[] tvUnits = new string[] { "02", "03", "04" };
            for (int i = 0; i < tvUnits.Length; i++) {
                list.Add(new Entry { group = "Retail", title = "TV Unit " + tvUnits[i], subtitle = "Retail  •  Living room", description = "A TV unit from the range, shown from the front and both sides.", prefabPath = RetailRoot + "TV_unit_" + tvUnits[i] + ".prefab", fit = 0.55f, tilt = -20.0f, sway = true, swayDeg = 36.0f });
            }
            string[] wardrobes = new string[] { "02", "03" };
            for (int i = 0; i < wardrobes.Length; i++) {
                list.Add(new Entry { group = "Retail", title = "Wardrobe " + wardrobes[i], subtitle = "Retail  •  Bedroom", description = "A wardrobe from the range, shown from the front and both sides.", prefabPath = RetailRoot + "Wardrobe_" + wardrobes[i] + ".prefab", fit = 0.69f, tilt = -20.0f, sway = true, swayDeg = 36.0f });
            }

            list.Add(new Entry { group = "Education", title = "Planet Earth", subtitle = "Education  •  Physics", description = "Earth turns once every 24 hours, giving us day and night.", prefabPath = EducationRoot + "Earth.prefab", show = PresentationShow.NativeMotion, fit = 0.74f, tilt = 8.0f, spin = 8.0f });
            list.Add(new Entry { group = "Education", title = "Biplane, 1909", subtitle = "Education  •  Technology", description = "Wind streams over the wings of an early biplane; the propeller spins and the arrows show lift, weight, thrust and drag.", prefabPath = AircraftPrefabPath, show = PresentationShow.Aircraft, sway = true, fit = 0.64f, tilt = 14.0f, spin = 22.0f });
            list.Add(new Entry { group = "Education", title = "Mona Lisa", subtitle = "Education  •  Arts", description = "Leonardo da Vinci's portrait, painted in the early 1500s and now in the Louvre.", prefabPath = EducationRoot + "MonaLisaFrame.prefab", sway = true, fit = 0.74f, tilt = 0.0f, spin = 18.0f });
            list.Add(new Entry { group = "Education", title = "Ethene, C<sub>2</sub>H<sub>4</sub>", subtitle = "Education  •  Chemistry", description = "Two carbon atoms joined by a double bond, each holding two hydrogens: the simplest alkene.", prefabPath = EducationRoot + "ChemistryThumb.prefab", pulse = true, fit = 0.64f, tilt = 12.0f });
            list.Add(new Entry { group = "Education", title = "Solar System", subtitle = "Education  •  Astronomy", description = "The Sun and its planets, each travelling its own orbit.", prefabPath = EducationRoot + "SolarSystemPreview.prefab", show = PresentationShow.NativeMotion, fit = 0.83f, tilt = 38.0f, spin = 5.0f });

            return list;
        }

        [MenuItem("KMax/Presentation/Build Presentation Scene")]
        public static void BuildScene() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Debug.LogError("[Presentation] Cannot build the scene while in play mode.");
                return;
            }

            string sceneDir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(sceneDir)) {
                Directory.CreateDirectory(sceneDir);
            }
            string prefabDir = Path.GetDirectoryName(AircraftPrefabPath);
            if (!Directory.Exists(prefabDir)) {
                Directory.CreateDirectory(prefabDir);
            }
            if (!File.Exists(ScenePath)) {
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath)) {
                    Debug.LogError("[Presentation] Failed to copy the base scene from " + SourceScenePath);
                    return;
                }
                AssetDatabase.Refresh();
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                string name = roots[i].name;
                if (name != "XRRig" && name != "Directional Light" && name != "Key Light" && name != "Lighting") {
                    Object.DestroyImmediate(roots[i]);
                }
            }

            if (!ExtractFromAircraftScene(scene)) {
                return;
            }

            List<Entry> catalog = BuildCatalog();
            List<PresentationItem> items = new List<PresentationItem>();
            for (int i = 0; i < catalog.Count; i++) {
                Entry e = catalog[i];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath);
                if (prefab == null) {
                    Debug.LogError("[Presentation] Missing prefab " + e.prefabPath + " for " + e.title + "; build aborted.");
                    return;
                }
                PresentationItem item = new PresentationItem();
                item.title = e.title;
                item.subtitle = e.subtitle;
                item.description = e.description;
                item.group = e.group;
                item.prefab = prefab;
                item.show = e.show;
                item.fitSize = e.fit;
                item.baseEuler = e.euler;
                item.tiltDegrees = e.tilt;
                item.spinDegreesPerSecond = e.spin;
                item.sway = e.sway;
                item.swayDegrees = e.swayDeg;
                item.pulse = e.pulse;
                item.anatomyTopic = e.anatomy;
                items.Add(item);
            }

            // Lighting exactly as the experience scenes: soft key, cool fill, bright rim, flat studio ambient (the earlier build was over-lit).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.45f, 1.0f);
            RenderSettings.reflectionIntensity = 0.35f;
            ConfigureLight("Key Light", "Directional Light", 1.15f, new Color(1.0f, 0.98f, 0.94f, 1.0f), Quaternion.Euler(34.0f, 200.0f, 0.0f), LightShadows.Soft);
            ConfigureLight("Fill Light", null, 0.5f, new Color(0.78f, 0.86f, 1.0f, 1.0f), Quaternion.Euler(16.0f, 300.0f, 0.0f), LightShadows.None);
            ConfigureLight("Rim Light", null, 0.95f, new Color(0.8f, 0.88f, 1.0f, 1.0f), Quaternion.Euler(18.0f, 40.0f, 0.0f), LightShadows.None);

            // A soft point light in front of the stage adds the specular highlight that makes glossy surfaces read as glossy.
            GameObject stageGo = new GameObject("StageLight");
            Light stage = stageGo.AddComponent<Light>();
            stage.type = LightType.Point;
            stage.intensity = 0.9f;
            stage.range = 9.0f;
            stage.color = new Color(0.92f, 0.96f, 1.0f, 1.0f);
            stage.shadows = LightShadows.None;
            stageGo.transform.position = new Vector3(0.6f, 1.6f, -2.6f);

            // The presentation reads no input: remove the viewer fly controller and stylus input, and hide the stylus tip and beam.
            ViewerFlyController fly = Object.FindFirstObjectByType<ViewerFlyController>();
            Transform rigRoot = null;
            if (fly != null) {
                SerializedObject flySo = new SerializedObject(fly);
                rigRoot = flySo.FindProperty("rigRoot").objectReferenceValue as Transform;
                Object.DestroyImmediate(fly);
            }
            if (rigRoot == null) {
                GameObject xrRigGo = GameObject.Find("XRRig");
                if (xrRigGo != null) {
                    rigRoot = xrRigGo.transform;
                }
            }

            Camera renderCam = null;
            if (rigRoot != null) {
                RemoveInputComponents(rigRoot);
                Transform[] all = rigRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++) {
                    if (all[i].name == "Tip" || all[i].name == "Beam") {
                        all[i].localScale = Vector3.zero;
                        Renderer r = all[i].GetComponent<Renderer>();
                        if (r != null) {
                            r.enabled = false;
                        }
                    }
                }
                Quaternion homeRot = Quaternion.Euler(8.0f, 0.0f, 0.0f);
                rigRoot.SetPositionAndRotation(homeRot * new Vector3(0.0f, 0.0f, 0.5f - 3.0f), homeRot);
                renderCam = rigRoot.GetComponentInChildren<Camera>(true);
                EditorUtility.SetDirty(rigRoot);
            } else {
                Debug.LogError("[Presentation] XRRig missing; the scene will have no camera.");
            }
            if (renderCam != null) {
                renderCam.clearFlags = CameraClearFlags.SolidColor;
                renderCam.backgroundColor = Backdrop;
            }

            BuildBackdrop();
            BuildThemeBackdrop();

            // Carousel stage: centre model 3 m from the camera (20% farther than the first pass, owner request).
            GameObject carousel = new GameObject("Carousel");
            carousel.transform.position = new Vector3(0.0f, 0.07f, 0.0f);

            MissionControlSceneBuilder.LoadGlassSprites();
            GameObject uiGo = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            MissionControlSceneBuilder.ConfigureWorldCanvas(uiGo, renderCam);
            GraphicRaycaster uiRaycaster = uiGo.GetComponent<GraphicRaycaster>();
            uiRaycaster.enabled = false;

            // One bottom-centre glass panel: title, sub-title, short description. No buttons anywhere.
            GameObject card = MissionControlSceneBuilder.CreateCaptionCard(uiGo.transform, "Description");
            card.name = "InfoPanel";
            CanvasGroup group = card.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            TextMeshProUGUI description = card.GetComponentInChildren<TextMeshProUGUI>();
            description.name = "Description";
            description.fontSize = 20.0f;
            TextMeshProUGUI title = CreatePanelText(card.transform, "Title", 34.0f, FontStyles.Bold, Color.white);
            TextMeshProUGUI subtitle = CreatePanelText(card.transform, "Subtitle", 20.0f, FontStyles.Normal, MissionControlSceneBuilder.Cyan);
            title.transform.SetSiblingIndex(1);
            subtitle.transform.SetSiblingIndex(2);
            description.transform.SetSiblingIndex(3);

            GameObject directorGo = new GameObject("PresentationDirector");
            PresentationDirector director = directorGo.AddComponent<PresentationDirector>();
            SerializedObject so = new SerializedObject(director);
            SerializedProperty list = so.FindProperty("items");
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("title").stringValue = items[i].title;
                element.FindPropertyRelative("subtitle").stringValue = items[i].subtitle;
                element.FindPropertyRelative("description").stringValue = items[i].description;
                element.FindPropertyRelative("group").stringValue = items[i].group;
                element.FindPropertyRelative("prefab").objectReferenceValue = items[i].prefab;
                element.FindPropertyRelative("show").enumValueIndex = (int)items[i].show;
                element.FindPropertyRelative("fitSize").floatValue = items[i].fitSize;
                element.FindPropertyRelative("baseEuler").vector3Value = items[i].baseEuler;
                element.FindPropertyRelative("tiltDegrees").floatValue = items[i].tiltDegrees;
                element.FindPropertyRelative("spinDegreesPerSecond").floatValue = items[i].spinDegreesPerSecond;
                element.FindPropertyRelative("sway").boolValue = items[i].sway;
                element.FindPropertyRelative("swayDegrees").floatValue = items[i].swayDegrees;
                element.FindPropertyRelative("pulse").boolValue = items[i].pulse;
                element.FindPropertyRelative("anatomyTopic").stringValue = items[i].anatomyTopic;
            }
            so.FindProperty("carouselRoot").objectReferenceValue = carousel.transform;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("subtitleText").objectReferenceValue = subtitle;
            so.FindProperty("descriptionText").objectReferenceValue = description;
            so.FindProperty("panelGroup").objectReferenceValue = group;
            so.FindProperty("neighbourX").floatValue = 1.1f;
            card.GetComponent<VerticalLayoutGroup>().spacing = 4.0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Presentation] Built " + ScenePath + " with " + items.Count + " models.");
        }

        /// <summary>
        /// From the AircraftFlight experience: the aircraft exhibit (model, propeller, wind streams, force arrows, concepts) saved as a prefab with its
        /// interface references cleared, and the post-processing volume copied into the open scene.
        /// </summary>
        private static bool ExtractFromAircraftScene(Scene target) {
            Scene source = EditorSceneManager.OpenScene(AircraftScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(target);

            GameObject exhibit = null;
            GameObject postFx = null;
            GameObject[] sourceRoots = source.GetRootGameObjects();
            for (int i = 0; i < sourceRoots.Length; i++) {
                if (sourceRoots[i].name == "AircraftExhibit") {
                    exhibit = sourceRoots[i];
                } else if (sourceRoots[i].name == "PostFX") {
                    postFx = sourceRoots[i];
                }
            }
            if (exhibit == null) {
                Debug.LogError("[Presentation] AircraftExhibit not found in " + AircraftScenePath);
                EditorSceneManager.CloseScene(source, true);
                return false;
            }

            if (postFx != null) {
                GameObject postCopy = Object.Instantiate(postFx);
                postCopy.name = "PostFX";
            }

            GameObject copy = Object.Instantiate(exhibit);
            copy.name = "PresentationAircraft";
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            MonoBehaviour[] behaviours = copy.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++) {
                if (behaviours[i] == null) {
                    continue;
                }
                string typeName = behaviours[i].GetType().Name;
                if (typeName == "AircraftExperience") {
                    SerializedObject aso = new SerializedObject(behaviours[i]);
                    aso.FindProperty("choices").arraySize = 0;
                    ClearReference(aso, "previousButton");
                    ClearReference(aso, "nextButton");
                    ClearReference(aso, "subtitleText");
                    ClearReference(aso, "counterText");
                    ClearReference(aso, "captionNameText");
                    ClearReference(aso, "captionBodyText");
                    ClearReference(aso, "formulaText");
                    ClearReference(aso, "symbolsText");
                    ClearReference(aso, "explanationText");
                    ClearReference(aso, "flyController");
                    aso.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            string[] drop = new string[] { "VehicleExhibitControls", "ExhibitPostProcessing", "EyeManipulator", "AnatomyStylusInput", "ViewerFlyController" };
            for (int d = 0; d < drop.Length; d++) {
                MonoBehaviour[] current = copy.GetComponents<MonoBehaviour>();
                for (int i = 0; i < current.Length; i++) {
                    if (current[i] != null && current[i].GetType().Name == drop[d]) {
                        Object.DestroyImmediate(current[i]);
                    }
                }
            }

            PrefabUtility.SaveAsPrefabAsset(copy, AircraftPrefabPath);
            Object.DestroyImmediate(copy);
            EditorSceneManager.CloseScene(source, true);
            Debug.Log("[Presentation] Extracted " + AircraftPrefabPath);
            return true;
        }

        private static void ClearReference(SerializedObject so, string propertyName) {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property != null) {
                property.objectReferenceValue = null;
            }
        }

        private const string EnvRoot = "Assets/Games/kmax-presentation/Environment/";

        private static Material EnvMaterial(string name, Texture2D albedo, Texture2D normal, Color tint, float smoothness, float normalScale) {
            string path = EnvRoot + "Materials/" + name + ".mat";
            if (!Directory.Exists(EnvRoot + "Materials")) {
                Directory.CreateDirectory(EnvRoot + "Materials");
            }
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0.0f);
            material.SetFloat("_Cull", 0.0f);
            if (normal != null) {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", normalScale);
                material.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetNormalMap(string texturePath) {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap) {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static GameObject EnvPart(Transform root, string modelFile, string name, Material material, Vector3 position, Vector3 euler, Vector3 scale) {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvRoot + "Models/" + modelFile);
            if (asset == null) {
                Debug.LogError("[Presentation] Environment model missing: " + modelFile);
                return null;
            }
            GameObject instance = (GameObject)Object.Instantiate(asset, root);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localEulerAngles = euler;
            instance.transform.localScale = scale;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) {
                renderers[i].sharedMaterial = material;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
            return instance;
        }

        /// <summary>
        /// The Virtual Showroom Space environment (owner supplied): flowing pattern wall, curved walls, roof dome and a glossy marble floor disc,
        /// as plain unlit-to-lit URP meshes behind the stage. The mote particles of the rig stay on and draw in front of it.
        /// </summary>
        private static void BuildBackdrop() {
            string[] normals = new string[] { "Base_Normal", "Platform_Normal", "Roof_Normal", "Wall1_Normal", "Wall2_Normal", "Concrete_Normal" };
            for (int i = 0; i < normals.Length; i++) {
                SetNormalMap(EnvRoot + "Textures/" + normals[i] + ".png");
            }
            Texture2D baseDiffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Base_Diffuse.png");
            Texture2D baseNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Base_Normal.png");
            Texture2D roofDiffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Roof_Diffuse.png");
            Texture2D roofNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Roof_Normal.png");
            Texture2D wall1Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Wall1_Normal.png");
            Texture2D wall2Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Wall2_Normal.png");

            Texture2D platformDiffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Platform_Diffuse.png");
            Texture2D platformNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Platform_Normal.png");
            Texture2D palmLeaf = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvRoot + "Textures/Palm_Leaf.png");

            // Part materials. The meshes carry no material of their own (the export had none), so they are matched by part name.
            Material floor = EnvMaterial("Showroom_Floor", null, null, new Color(0.035f, 0.04f, 0.055f, 1.0f), 0.88f, 1.0f);
            Material roof = EnvMaterial("Showroom_Roof", roofDiffuse, roofNormal, new Color(0.62f, 0.6f, 0.62f, 1.0f), 0.3f, 1.0f);
            Material golden = EnvMaterial("Showroom_GoldenWall", null, wall1Normal, new Color(0.78f, 0.58f, 0.2f, 1.0f), 0.25f, 0.5f);
            Material fins = EnvMaterial("Showroom_FinWall", null, wall2Normal, new Color(0.72f, 0.76f, 0.82f, 1.0f), 0.45f, 0.5f);
            Material platform = EnvMaterial("Showroom_Platform", platformDiffuse, platformNormal, new Color(0.9f, 1.0f, 1.0f, 1.0f), 0.8f, 0.6f);
            Material pot = EnvMaterial("Showroom_Pot", null, null, new Color(0.62f, 0.08f, 0.08f, 1.0f), 0.55f, 1.0f);
            Material palm = EnvMaterial("Showroom_Palm", palmLeaf, null, Color.white, 0.2f, 1.0f);
            palm.SetFloat("_AlphaClip", 1.0f);
            palm.SetFloat("_Cutoff", 0.5f);
            palm.EnableKeyword("_ALPHATEST_ON");
            palm.renderQueue = 2450;
            EditorUtility.SetDirty(palm);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvRoot + "Showroom_Arranged.fbx");
            if (asset == null) {
                Debug.LogError("[Presentation] Showroom_Arranged.fbx missing; the arranged environment is skipped.");
                return;
            }

            // The FBX was arranged in Blender from the owner's Sketchfab showroom: ring of golden wall + fin wall closing it, roof dome with skylight,
            // glossy floor with its top at 1.64 m, notched and round teal platforms, pot and palm. Its floor level maps to the stage floor.
            GameObject rootGo = (GameObject)Object.Instantiate(asset);
            rootGo.name = "Environment";
            rootGo.transform.localScale = Vector3.one * EnvScale;
            rootGo.transform.position = new Vector3(0.0f, EnvFloorY - EnvModelFloorHeight * EnvScale, 0.0f);
            rootGo.transform.rotation = Quaternion.Euler(0.0f, EnvYaw, 0.0f);

            Renderer[] renderers = rootGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) {
                string partName = renderers[i].gameObject.name;
                Material material = null;
                if (partName.StartsWith("Floor")) {
                    material = floor;
                } else if (partName.StartsWith("GoldenWall")) {
                    material = golden;
                } else if (partName.StartsWith("FinWall")) {
                    material = fins;
                } else if (partName.StartsWith("Roof")) {
                    material = roof;
                } else if (partName.StartsWith("Platform")) {
                    material = platform;
                } else if (partName.StartsWith("Pot")) {
                    material = pot;
                } else if (partName.StartsWith("Palm")) {
                    material = palm;
                } else {
                    Debug.LogWarning("[Presentation] Environment part '" + partName + "' has no material rule.");
                    continue;
                }
                renderers[i].sharedMaterial = material;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }

        private const float EnvYaw = 180.0f;
        private const float EnvModelFloorHeight = 1.64f;
        private const float EnvScale = 0.5f;
        private const float EnvFloorRadius = 4.8f;
        private const float EnvRoomRadius = 24.0f;
        private const float EnvFloorY = -0.7f;
        private const float EnvFlowScale = 0.46f;

        /// <summary>
        /// The owner's Backdrop quad (position (0, -1.3, 9), scale (9, 5, 1)) with the perspective-grid wallpaper recoloured to the Design.md backdrop
        /// slate #252A36 instead of black, so the grid lines read as soft slate-cyan on the theme colour.
        /// </summary>
        private static void BuildThemeBackdrop() {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ThemeBackdropTexturePath);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (texture == null || shader == null) {
                Debug.LogError("[Presentation] Theme backdrop texture or URP Unlit shader missing; Backdrop skipped.");
                return;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(ThemeBackdropMaterialPath);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, ThemeBackdropMaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.position = new Vector3(0.0f, -1.3f, 9.0f);
            quad.transform.rotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(9.0f, 5.0f, 1.0f);
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static TextMeshProUGUI CreatePanelText(Transform parent, string name, float size, FontStyles style, Color color) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = name;
            return tmp;
        }

        private static void ConfigureLight(string name, string fallbackName, float intensity, Color color, Quaternion rotation, LightShadows shadows) {
            GameObject go = GameObject.Find(name);
            if (go == null && fallbackName != null) {
                go = GameObject.Find(fallbackName);
            }
            if (go == null) {
                go = new GameObject(name);
                go.AddComponent<Light>();
            }
            go.name = name;
            Light light = go.GetComponent<Light>();
            if (light == null) {
                light = go.AddComponent<Light>();
            }
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = shadows;
            go.transform.rotation = rotation;
        }

        private static void RemoveInputComponents(Transform rigRoot) {
            MonoBehaviour[] behaviours = rigRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) {
                if (behaviours[i] == null) {
                    continue;
                }
                string typeName = behaviours[i].GetType().Name;
                if (typeName == "AnatomyStylusInput" || typeName == "ViewerFlyController") {
                    Object.DestroyImmediate(behaviours[i]);
                }
            }
        }
    }
}
