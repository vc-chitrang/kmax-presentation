using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation {
    public enum PresentationShow {
        Turntable,
        Explode,
        EngineRun,
        NativeMotion,
        Aircraft
    }

    [Serializable]
    public class PresentationItem {
        public string title;
        public string subtitle;
        public string description;
        public string group;
        public GameObject prefab;
        public PresentationShow show;
        public float fitSize = 1.0f;
        public Vector3 baseEuler = Vector3.zero;
        public float tiltDegrees = 8.0f;
        public float spinDegreesPerSecond = 26.0f;
        public bool sway;
        public float swayDegrees = 32.0f;
        public bool pulse;
        public string anatomyTopic;
    }

    /// <summary>
    /// Drives the Presentation Scene: a carousel of models in a smart random order (a shuffled bag, never the same model twice inside the
    /// visible window, never two of one section in a row where avoidable), one model at the centre at a time, each playing its own
    /// automatic show-off while its own native animation keeps running, with one information panel that follows the centre model.
    /// No input of any kind is read.
    /// </summary>
    public class PresentationDirector : MonoBehaviour {
        private const float MoveSeconds = 1.4f;
        private const float ShowSeconds = 8.0f;
        private const int LoadRadius = 2;
        private const float AircraftConceptSeconds = 2.7f;

        [SerializeField] private List<PresentationItem> items = new List<PresentationItem>();
        [SerializeField] private Transform carouselRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private float neighbourX = 1.1f;
        [SerializeField] private float explodeSpread = 0.28f;
        [SerializeField] private float maxFootprint = 1.35f;

        private sealed class Slot {
            public int queueIndex;
            public PresentationItem item;
            public Transform holder;
            public Transform spinner;
            public Transform model;
            public List<ExplodePart> parts = new List<ExplodePart>();
            public float partSpread;
            public float explodeGrow = 1.0f;
            public float spinAngle;
            public Vector3 baseScale;
            public Component engine;
            public FieldInfo engineRpm;
            public Component aircraft;
            public MethodInfo aircraftShow;
            public int aircraftConceptCount;
            public int aircraftConcept = -1;
        }

        private sealed class ExplodePart {
            public Transform transform;
            public Vector3 baseLocalPosition;
            public Vector3 direction;
        }

        private readonly List<int> queue = new List<int>();
        private readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();
        private float carouselPosition;
        private int currentIndex;
        private int fromIndex;
        private float moveTimer = MoveSeconds;
        private float itemTimer;
        private bool textSwapped = true;
        private Transform inactiveParent;

        private void Start() {
            if (items.Count == 0) {
                Debug.LogError("[Presentation] No items configured; nothing to present.");
                enabled = false;
                return;
            }

            GameObject parkGo = new GameObject("InactiveParking");
            parkGo.transform.SetParent(transform, false);
            parkGo.SetActive(false);
            inactiveParent = parkGo.transform;

            EnsureQueue(LoadRadius + 12);
            currentIndex = 0;
            fromIndex = 0;
            carouselPosition = 0.0f;
            moveTimer = MoveSeconds;
            itemTimer = 0.0f;
            ApplyText(items[queue[0]]);
            SetPanelAlpha(1.0f);
            EnsureWindow(true);
            LayoutSlots();
        }

        private void Update() {
            float dt = Time.deltaTime;
            itemTimer += dt;

            if (moveTimer < MoveSeconds) {
                moveTimer += dt;
                float k = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(moveTimer / MoveSeconds));
                carouselPosition = Mathf.Lerp(fromIndex, currentIndex, k);
                UpdatePanelFade();
            } else {
                carouselPosition = currentIndex;
                SetPanelAlpha(1.0f);
            }

            if (itemTimer >= ShowSeconds) {
                Advance();
            }

            EnsureWindow(false);
            LayoutSlots();
            AnimateSlots(dt);
        }

        private void Advance() {
            fromIndex = currentIndex;
            currentIndex = currentIndex + 1;
            EnsureQueue(currentIndex + LoadRadius + 12);
            moveTimer = 0.0f;
            itemTimer = 0.0f;
            textSwapped = false;
        }

        /// <summary>Appends shuffled batches until the queue is long enough; see <see cref="AppendBatch"/>.</summary>
        private void EnsureQueue(int length) {
            while (queue.Count < length) {
                AppendBatch();
            }
        }

        /// <summary>
        /// One pass through every item in random order. Each pick avoids anything shown in the last few turns and avoids the section of
        /// the previous pick; when no candidate satisfies both, the first that avoids the recent ones is taken. So no model repeats before
        /// the whole set has been shown, and the section changes at every step wherever the set allows.
        /// </summary>
        private void AppendBatch() {
            List<int> pool = new List<int>();
            for (int i = 0; i < items.Count; i++) {
                pool.Add(i);
            }
            for (int i = pool.Count - 1; i > 0; i--) {
                int j = UnityEngine.Random.Range(0, i + 1);
                int swap = pool[i];
                pool[i] = pool[j];
                pool[j] = swap;
            }

            while (pool.Count > 0) {
                string previousGroup = queue.Count > 0 ? items[queue[queue.Count - 1]].group : null;
                int pick = -1;
                int fallback = -1;
                for (int p = 0; p < pool.Count; p++) {
                    int candidate = pool[p];
                    if (IsRecent(candidate)) {
                        continue;
                    }
                    if (fallback < 0) {
                        fallback = p;
                    }
                    if (items[candidate].group != previousGroup) {
                        pick = p;
                        break;
                    }
                }
                if (pick < 0) {
                    pick = fallback >= 0 ? fallback : 0;
                }
                queue.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
        }

        private bool IsRecent(int itemIndex) {
            // A model returns only after about half the set has been shown (minutes, not seconds).
            int window = Mathf.Clamp(items.Count / 2, 4, Mathf.Max(4, items.Count - 4));
            int start = Mathf.Max(0, queue.Count - window);
            for (int i = start; i < queue.Count; i++) {
                if (queue[i] == itemIndex) {
                    return true;
                }
            }
            return false;
        }

        private void UpdatePanelFade() {
            float t = moveTimer;
            float half = MoveSeconds * 0.5f;
            if (t < half) {
                SetPanelAlpha(1.0f - Mathf.Clamp01(t / half));
                return;
            }
            if (!textSwapped) {
                ApplyText(items[queue[currentIndex]]);
                textSwapped = true;
            }
            SetPanelAlpha(Mathf.Clamp01((t - half) / half));
        }

        private void SetPanelAlpha(float alpha) {
            if (panelGroup != null) {
                panelGroup.alpha = alpha;
            }
        }

        private void ApplyText(PresentationItem item) {
            if (titleText != null) {
                titleText.text = item.title;
            }
            if (subtitleText != null) {
                subtitleText.text = item.subtitle;
            }
            if (descriptionText != null) {
                descriptionText.text = item.description;
            }
        }

        private void EnsureWindow(bool all) {
            bool created = false;
            for (int step = 0; step <= LoadRadius * 2; step++) {
                int d = (step + 1) / 2;
                if (step % 2 == 0) {
                    d = -d;
                }
                int q = currentIndex + d;
                if (q < 0 || slots.ContainsKey(q)) {
                    continue;
                }
                if (created && !all) {
                    break;
                }
                Slot slot = CreateSlot(q);
                if (slot != null) {
                    slots[q] = slot;
                }
                created = true;
            }

            List<int> remove = new List<int>();
            foreach (KeyValuePair<int, Slot> pair in slots) {
                if (Mathf.Abs(pair.Key - carouselPosition) > LoadRadius + 1.5f) {
                    remove.Add(pair.Key);
                }
            }
            for (int i = 0; i < remove.Count; i++) {
                Destroy(slots[remove[i]].holder.gameObject);
                slots.Remove(remove[i]);
            }
        }

        private Slot CreateSlot(int queueIndex) {
            PresentationItem item = items[queue[queueIndex]];
            if (item.prefab == null) {
                Debug.LogError("[Presentation] " + item.title + " has no prefab.");
                return null;
            }

            GameObject holderGo = new GameObject("Slot_" + queueIndex + "_" + item.title);
            holderGo.transform.SetParent(carouselRoot != null ? carouselRoot : transform, false);
            GameObject spinnerGo = new GameObject("Spinner");
            spinnerGo.transform.SetParent(holderGo.transform, false);

            // Instantiated under an inactive parent so no gameplay Awake runs before the interactive scripts are stripped.
            GameObject model = Instantiate(item.prefab, inactiveParent);
            model.name = item.prefab.name;
            bool anatomy = !string.IsNullOrEmpty(item.anatomyTopic);
            StripInteraction(model, anatomy);
            model.transform.SetParent(spinnerGo.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(item.baseEuler);
            model.transform.localScale = Vector3.one;

            Slot slot = new Slot();
            slot.queueIndex = queueIndex;
            slot.item = item;
            slot.holder = holderGo.transform;
            slot.spinner = spinnerGo.transform;
            slot.model = model.transform;

            FitModel(slot, item.fitSize);
            slot.baseScale = slot.model.localScale;

            if (item.show == PresentationShow.Explode) {
                BuildExplodeParts(slot);
            }
            if (item.show == PresentationShow.EngineRun) {
                BindEngine(slot);
            }
            if (item.show == PresentationShow.Aircraft) {
                BindAircraft(slot);
            }
            if (item.title == "LM358 Fader" || model.name.Contains("LM358")) {
                BindPcbExhibit(slot);
            }
            MakeDoubleSided(model);
            if (anatomy) {
                AttachAnatomyBehaviour(model, item.anatomyTopic);
            }
            holderGo.transform.localRotation = Quaternion.Euler(item.tiltDegrees, 0.0f, 0.0f);
            holderGo.SetActive(false);
            return slot;
        }

        private static void BindPcbExhibit(Slot slot) {
            Component exhibit = slot.model.GetComponent("PcbWorkingExhibit");
            if (exhibit != null) {
                return;
            }
            Type type = FindType("ViitorCloud.KMaxPCBAssembly.PcbWorkingExhibit");
            if (type != null) {
                slot.model.gameObject.AddComponent(type);
            }
        }

        private static readonly string[] KeepBehaviours = new string[] {
            "EarthRotation",
            "SolarOrbitPreview",
            "Enginei4",
            "AircraftExperience",
            "WindStreams",
            "ForceArrow",
            "AirFoil",
            "PcbWorkingExhibit"
        };

        private static void StripInteraction(GameObject model, bool keepAll) {
            if (!keepAll) {
                MonoBehaviour[] behaviours = model.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++) {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null) {
                        continue;
                    }
                    string typeName = behaviour.GetType().Name;
                    bool keep = false;
                    for (int k = 0; k < KeepBehaviours.Length; k++) {
                        if (typeName == KeepBehaviours[k]) {
                            keep = true;
                            break;
                        }
                    }
                    if (!keep) {
                        DestroyImmediate(behaviour);
                    }
                }
            }

            Rigidbody[] bodies = model.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++) {
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
            }

            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) {
                colliders[i].enabled = false;
            }
        }

        private static Type FindType(string fullName) {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++) {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null) {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Starts the topic's own animation the way the Anatomy launcher preview does: the heart beats, the chest breathes, the ear hears,
        /// the body map glows, the eye glances about, the jaw opens. Done by name so this assembly does not depend on the Anatomy one.
        /// </summary>
        private static void AttachAnatomyBehaviour(GameObject model, string topicId) {
            try {
                Type library = FindType("ViitorCloud.KmaxAnatomy.AnatomyTopicLibrary");
                Type behaviours = FindType("ViitorCloud.KmaxAnatomy.AnatomyBehaviours");
                if (library == null || behaviours == null) {
                    Debug.LogWarning("[Presentation] Anatomy assembly not found; " + topicId + " stays still.");
                    return;
                }

                object data = library.GetMethod("LoadData", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { topicId });
                if (data == null) {
                    return;
                }
                AttachHighlights(model, data);
                PropertyInfo keyProperty = data.GetType().GetProperty("Behaviour");
                string key = keyProperty != null ? (string)keyProperty.GetValue(data, null) : string.Empty;
                if (string.IsNullOrEmpty(key)) {
                    return;
                }

                if (key == "eye" || key == "jaw") {
                    Type behaviourType = FindType(key == "eye" ? "ViitorCloud.KmaxAnatomy.EyeBehaviour" : "ViitorCloud.KmaxAnatomy.JawBehaviour");
                    if (behaviourType == null) {
                        return;
                    }
                    Component behaviour = model.AddComponent(behaviourType);
                    behaviourType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Instance).Invoke(behaviour, null);
                    return;
                }

                behaviours.GetMethod("AttachForPreview", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { model, key });
            } catch (Exception exception) {
                Debug.LogWarning("[Presentation] Could not start the " + topicId + " animation: " + exception.Message);
            }
        }

        /// <summary>Gives every structure its highlight, as the Anatomy launcher preview does; the eye and jaw drive their muscles through it.</summary>
        private static void AttachHighlights(GameObject model, object data) {
            Type highlight = FindType("ViitorCloud.KmaxAnatomy.StructureHighlight");
            Component anatomyModel = model.GetComponent("AnatomyModel");
            if (highlight == null || anatomyModel == null) {
                return;
            }
            MethodInfo attach = highlight.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static);
            MethodInfo find = data.GetType().GetMethod("FindStructure", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo structuresProperty = anatomyModel.GetType().GetProperty("Structures");
            System.Collections.IEnumerable structures = structuresProperty != null ? structuresProperty.GetValue(anatomyModel, null) as System.Collections.IEnumerable : null;
            if (attach == null || find == null || structures == null) {
                return;
            }
            foreach (object structure in structures) {
                PropertyInfo idProperty = structure.GetType().GetProperty("StructureId");
                string id = idProperty != null ? (string)idProperty.GetValue(structure, null) : null;
                object info = find.Invoke(data, new object[] { id });
                attach.Invoke(null, new object[] { structure, info });
            }
        }

        private static readonly int CullId = Shader.PropertyToID("_Cull");

        /// <summary>Every surface draws both faces, so a cut-away or an open shell never shows its back as a hole.</summary>
        private static void MakeDoubleSided(GameObject model) {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) {
                    continue;
                }
                Material[] materials = renderers[i].materials;
                for (int m = 0; m < materials.Length; m++) {
                    if (materials[m] != null && materials[m].HasProperty(CullId)) {
                        materials[m].SetFloat(CullId, 0.0f);
                    }
                }
            }
        }

        private static Bounds MeshBounds(Transform root, out bool found) {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(root.position, Vector3.zero);
            found = false;
            for (int i = 0; i < renderers.Length; i++) {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) {
                    continue;
                }
                if (!found) {
                    bounds = renderers[i].bounds;
                    found = true;
                } else {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            return bounds;
        }

        private void FitModel(Slot slot, float fitSize) {
            slot.holder.localPosition = Vector3.zero;
            slot.holder.localRotation = Quaternion.identity;
            slot.holder.localScale = Vector3.one;

            bool found;
            Bounds bounds = MeshBounds(slot.model, out found);
            if (!found) {
                Debug.LogWarning("[Presentation] " + slot.model.name + " has no mesh renderers; left unscaled.");
                return;
            }

            // fitSize is the tallest the model may stand; the footprint cap keeps a wide model (a car, a bed, a biplane) inside the frame as it turns.
            float footprint = Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z);
            float byHeight = bounds.size.y > 0.0001f ? fitSize / bounds.size.y : float.MaxValue;
            float byFootprint = footprint > 0.0001f ? maxFootprint / footprint : float.MaxValue;
            float scale = Mathf.Min(byHeight, byFootprint);
            if (scale > 100000.0f) {
                scale = 1.0f;
            }
            // 5% top and bottom padding on each edge for 3D models.
            const float EdgePadding = 0.90f;
            scale = scale * EdgePadding;
            slot.model.localScale = slot.model.localScale * scale;

            Bounds scaled = MeshBounds(slot.model, out found);
            Vector3 centreLocal = slot.spinner.InverseTransformPoint(scaled.center);
            slot.model.localPosition -= centreLocal;
        }

        private void BuildExplodeParts(Slot slot) {
            Transform parent = slot.model;
            for (int depth = 0; depth < 4; depth++) {
                if (parent.childCount == 1) {
                    parent = parent.GetChild(0);
                } else {
                    break;
                }
            }

            bool found;
            Bounds total = MeshBounds(slot.model, out found);
            if (!found) {
                return;
            }

            slot.partSpread = Mathf.Max(total.size.x, Mathf.Max(total.size.y, total.size.z)) * explodeSpread;
            for (int i = 0; i < parent.childCount; i++) {
                Transform child = parent.GetChild(i);
                bool childFound;
                Bounds partBounds = MeshBounds(child, out childFound);
                if (!childFound) {
                    continue;
                }
                Vector3 worldDirection = partBounds.center - total.center;
                if (worldDirection.sqrMagnitude < 0.0000001f) {
                    float angle = i * 2.399963f;
                    worldDirection = new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle));
                }
                ExplodePart part = new ExplodePart();
                part.transform = child;
                part.baseLocalPosition = child.localPosition;
                part.direction = worldDirection.normalized;
                slot.parts.Add(part);
            }

            // Measure the fully open model once, so the show can draw it back exactly as far as it grows and never leave the frame.
            ApplyExplode(slot, 1.0f);
            bool openFound;
            Bounds open = MeshBounds(slot.model, out openFound);
            ApplyExplode(slot, 0.0f);
            if (openFound && total.size.y > 0.0001f) {
                float growY = open.size.y / total.size.y;
                float growFootprint = Mathf.Sqrt(open.size.x * open.size.x + open.size.z * open.size.z) / Mathf.Max(0.0001f, Mathf.Sqrt(total.size.x * total.size.x + total.size.z * total.size.z));
                slot.explodeGrow = Mathf.Max(1.0f, Mathf.Max(growY, growFootprint));
            }
        }

        private void BindEngine(Slot slot) {
            Component[] components = slot.model.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++) {
                if (components[i] != null && components[i].GetType().Name == "Enginei4") {
                    slot.engine = components[i];
                    slot.engineRpm = components[i].GetType().GetField("RPM", BindingFlags.Public | BindingFlags.Instance);
                    break;
                }
            }
            if (slot.engine == null || slot.engineRpm == null) {
                Debug.LogWarning("[Presentation] EngineRun item has no Enginei4 RPM; showing a turntable instead.");
            }
        }

        private void BindAircraft(Slot slot) {
            Component[] components = slot.model.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++) {
                if (components[i] != null && components[i].GetType().Name == "AircraftExperience") {
                    slot.aircraft = components[i];
                    slot.aircraftShow = components[i].GetType().GetMethod("ShowConcept", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo concepts = components[i].GetType().GetField("concepts", BindingFlags.NonPublic | BindingFlags.Instance);
                    Array list = concepts != null ? concepts.GetValue(components[i]) as Array : null;
                    slot.aircraftConceptCount = list != null ? list.Length : 0;
                    break;
                }
            }
            if (slot.aircraft == null || slot.aircraftShow == null || slot.aircraftConceptCount == 0) {
                Debug.LogWarning("[Presentation] Aircraft item has no AircraftExperience concepts; the wind will not change.");
            }
        }

        private void LayoutSlots() {
            foreach (KeyValuePair<int, Slot> pair in slots) {
                Slot slot = pair.Value;
                float offset = pair.Key - carouselPosition;
                float a = Mathf.Abs(offset);
                bool visible = a < 2.6f;
                if (slot.holder.gameObject.activeSelf != visible) {
                    slot.holder.gameObject.SetActive(visible);
                }
                if (!visible) {
                    continue;
                }

                float sign = Mathf.Sign(offset);
                float x;
                float scale;
                if (a <= 1.0f) {
                    x = a * neighbourX;
                    scale = Mathf.Lerp(1.0f, 0.42f, a);
                } else {
                    x = neighbourX + (a - 1.0f) * 0.8f;
                    scale = Mathf.Lerp(0.42f, 0.28f, Mathf.Clamp01(a - 1.0f));
                }
                slot.holder.localPosition = new Vector3(sign * x, -0.1f * Mathf.Min(a, 1.0f), a * 0.9f);
                slot.holder.localScale = Vector3.one * scale;
            }
        }

        private void AnimateSlots(float dt) {
            foreach (KeyValuePair<int, Slot> pair in slots) {
                Slot slot = pair.Value;
                if (!slot.holder.gameObject.activeSelf) {
                    continue;
                }
                bool isCentre = pair.Key == currentIndex;
                AnimateSlot(slot, dt, isCentre);
            }
        }

        private void AnimateSlot(Slot slot, float dt, bool isCentre) {
            PresentationItem item = slot.item;
            float t = isCentre ? itemTimer : 0.0f;

            // Neighbours keep turning slowly so the whole stage stays alive; the centre plays its full show.
            float speed = isCentre ? item.spinDegreesPerSecond : item.spinDegreesPerSecond * 0.5f;
            slot.spinAngle += speed * dt;
            float angle = slot.spinAngle;
            if (item.sway) {
                angle = Mathf.Sin(slot.spinAngle * Mathf.Deg2Rad * 0.9f) * item.swayDegrees;
            }
            slot.spinner.localRotation = Quaternion.Euler(0.0f, angle, 0.0f);

            if (item.pulse) {
                float beat = 1.0f + Mathf.Sin(Time.time * 2.4f) * 0.012f + Mathf.Sin(Time.time * 5.1f) * 0.004f;
                slot.model.localScale = slot.baseScale * beat;
            }

            if (item.show == PresentationShow.Explode && slot.parts.Count > 0) {
                float factor = isCentre ? ExplodeCurve(t) : 0.0f;
                ApplyExplode(slot, factor);
                // The model draws back a little as it opens so the exploded view stays inside the frame.
                slot.spinner.localScale = Vector3.one / Mathf.Lerp(1.0f, slot.explodeGrow, factor);
            } else if (item.show == PresentationShow.EngineRun && slot.engineRpm != null) {
                float rpm = Mathf.Lerp(6.0f, 26.0f, Mathf.PingPong(Time.time / 3.5f, 1.0f));
                slot.engineRpm.SetValue(slot.engine, rpm);
            } else if (item.show == PresentationShow.Aircraft && slot.aircraftShow != null && slot.aircraftConceptCount > 0) {
                int concept = Mathf.FloorToInt(Time.time / AircraftConceptSeconds) % slot.aircraftConceptCount;
                if (concept != slot.aircraftConcept) {
                    slot.aircraftConcept = concept;
                    slot.aircraftShow.Invoke(slot.aircraft, new object[] { concept });
                }
            }

            // Slow bob keeps still-life items alive between turns.
            if (isCentre) {
                Vector3 p = slot.holder.localPosition;
                p.y += Mathf.Sin(itemTimer * 1.3f) * 0.015f;
                slot.holder.localPosition = p;
            }
        }

        private static float ExplodeCurve(float t) {
            if (t < 1.8f) {
                return 0.0f;
            }
            if (t < 3.8f) {
                return Mathf.SmoothStep(0.0f, 1.0f, (t - 1.8f) / 2.0f);
            }
            if (t < 5.2f) {
                return 1.0f;
            }
            if (t < 7.2f) {
                return Mathf.SmoothStep(1.0f, 0.0f, (t - 5.2f) / 2.0f);
            }
            return 0.0f;
        }

        private static void ApplyExplode(Slot slot, float factor) {
            for (int i = 0; i < slot.parts.Count; i++) {
                ExplodePart part = slot.parts[i];
                if (part.transform == null) {
                    continue;
                }
                Vector3 worldDelta = slot.spinner.TransformDirection(part.direction) * (slot.partSpread * factor);
                Vector3 localDelta = part.transform.parent.InverseTransformVector(worldDelta);
                part.transform.localPosition = part.baseLocalPosition + localDelta;
            }
        }
    }
}
