using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using KmaxXR;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        [Tooltip("One short line shown under the title. Empty: the full description is shown.")]
        public string shortDescription;
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

        [Tooltip("Manual size over the automatic fit (owner's tuning). Changes show at once in Play; press Save scales on the director to keep them. Saved values are loaded from the tuning asset when the scene starts.")]
        [Range(PresentationTuning.MinManualScale, PresentationTuning.MaxManualScale)]
        public float scale = 1.0f;
    }

    /// <summary>
    /// Drives the Presentation Scene: a carousel of models in a smart random order (a shuffled bag, never the same model twice inside the
    /// visible window, never two of one section in a row where avoidable), one model at the centre at a time, each playing its own
    /// automatic show-off while its own native animation keeps running, with one information panel that follows the centre model.
    /// No input of any kind is read.
    /// </summary>
    public class PresentationDirector : MonoBehaviour {
        private const float PopInSeconds = 0.24f;
        private const float PopOutSeconds = 0.24f;
        private const float PanelDelaySeconds = 0.1f;
        private const float PanelFadeSeconds = 0.24f;
        private const float ShowSeconds = 8.0f;
        private const int LoadRadius = 1;
        private const float ShadowPadding = 1.30f;
        private const float ShadowMaxAlpha = 0.65f;
        private MaterialPropertyBlock shadowBlock;
        private const float AircraftConceptSeconds = 2.7f;

        [SerializeField] private List<PresentationItem> items = new List<PresentationItem>();
        [SerializeField] private Transform carouselRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Material floorShadowMaterial;
        [Tooltip("Dark contact quad under each model. Off: the models float on the backdrop with only their own soft shadows.")]
        [SerializeField] private bool showFloorShadow = false;
        [SerializeField] private float explodeSpread = 0.28f;
        [SerializeField] private float maxFootprint = 1.35f;
        [SerializeField] private Transform modelPlacement;
        [Tooltip("Fit and place every model in the display's own comfort volume (rig at the models, glass through them). Off: models keep the fit by screen size at the Carousel, as in the older scenes.")]
        [SerializeField] private bool stereoFit = false;
        [Tooltip("Where the model's centre sits on the view axis, in metres at view scale 1 (multiplied by the rig's view scale). 0 = on the glass, so half of it comes out of the screen; positive = deeper, negative = further out.")]
        [SerializeField] private float stageCentre = 0.0f;
        [SerializeField] private Camera fitCamera;
        [SerializeField] private Transform shadowFloor;
        [SerializeField] [Range(0.5f, 1.0f)] private float fitHeightFraction = 0.60f;
        [SerializeField] [Range(0.3f, 1.0f)] private float fitWidthFraction = 0.85f;
        [SerializeField] private bool autoAdvance = true;
        [SerializeField] private bool debugBrowse = true;
        [SerializeField, Tooltip("Retunes the Key, Fill and Rim lights for each model. Off keeps the scene's own lights as they are built.")]
        private bool perItemLighting = true;
        [SerializeField, Tooltip("Keeps the stage at the carousel's origin instead of in front of the rig, for a scene whose viewer orbits the rig around the model.")]
        private bool anchorStageToOrigin;
        [SerializeField, Tooltip("Saved per-model tuning. Its manual scales are loaded into the items when the scene starts; Save scales writes them back.")]
        private PresentationTuning tuning;

        private int fitVersion;

        public PresentationTuning Tuning {
            get {
                return tuning;
            }
        }

        /// <summary>Goes up each time the model on stage is fitted again without being reloaded (a manual scale changed), so placement can follow.</summary>
        public int FitVersion {
            get {
                return fitVersion;
            }
        }

        public bool AutoAdvance {
            get {
                return autoAdvance;
            }
            set {
                autoAdvance = value;
            }
        }

        private Transform floorShadowTransform;
        private MeshRenderer floorShadowRenderer;
        private Light keyLight;
        private Light fillLight;
        private Light rimLight;
        private Light stageLight;
        private ReflectionProbe showroomProbe;

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
            public Vector2 footprint;
            public Component engine;
            public FieldInfo engineRpm;
            public Component aircraft;
            public MethodInfo aircraftShow;
            public int aircraftConceptCount;
            public int aircraftConcept = -1;
            public Sequence pop;
            public bool popStarted;
            public bool refitRequested;
            public float appliedScale = 1.0f;
        }

        private sealed class ExplodePart {
            public Transform transform;
            public Vector3 baseLocalPosition;
            public Vector3 direction;
        }

        private readonly List<int> queue = new List<int>();
        private readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();
        private int currentIndex;
        private int browseIndex;
        private const float SceneShadowMargin = 1.5f;
        // The display's comfort volume, from the Kmax SDK (StereoCamera.DefaultDistance and the 0.37 / 0.80 of XRRig.DrawFrustum),
        // all multiplied by the rig's view scale: 0.13 m of pop-out in front of the glass and 0.30 m of depth behind it at scale 1.
        private const float ComfortNearFromCamera = 0.37f;
        private const float ComfortFarFromCamera = 0.80f;
        private const float ComfortMargin = 0.015f;
        private const float BobMetres = 0.0025f;
        private const float BobRadiansPerSecond = 0.45f;
        private const float WindowFill = 0.90f;
        private UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset shadowAsset;
        private float originalShadowDistance;
        private bool shadowDistanceChanged;
        private PresentationDebugBrowser debugBrowser;
        private Tween browseTween;
        private bool firstBrowseRefitDone;
        private Transform stereoRig;
        private float itemTimer;
        private Transform inactiveParent;
        private Sequence panelSequence;

        private void Start() {
            if (items.Count == 0) {
                Debug.LogError("[Presentation] No items configured; nothing to present.");
                enabled = false;
                return;
            }

            LoadSavedScales();
            ResolvePlacement();

            if (carouselRoot != null) {
                for (int i = carouselRoot.childCount - 1; i >= 0; i--) {
                    Transform staleChild = carouselRoot.GetChild(i);
                    staleChild.gameObject.SetActive(false);
                    Destroy(staleChild.gameObject);
                }
            }

            GameObject parkGo = new GameObject("InactiveParking");
            parkGo.transform.SetParent(transform, false);
            parkGo.SetActive(false);
            inactiveParent = parkGo.transform;

            if (floorShadowMaterial == null) {
#if UNITY_EDITOR
                floorShadowMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Games/kmax-presentation/Environment/Materials/M_Showroom_FloorShadow.mat");
#endif
            }

            if (showFloorShadow && floorShadowMaterial != null) {
                GameObject shadowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                shadowQuad.name = "FloorContactShadow";
                shadowQuad.transform.SetParent(transform, false);
                shadowQuad.transform.position = new Vector3(0.0f, -0.695f, 0.0f);
                shadowQuad.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                Collider shadowCol = shadowQuad.GetComponent<Collider>();
                if (shadowCol != null) {
                    Destroy(shadowCol);
                }
                floorShadowRenderer = shadowQuad.GetComponent<MeshRenderer>();
                floorShadowRenderer.sharedMaterial = floorShadowMaterial;
                floorShadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                floorShadowRenderer.receiveShadows = false;
                floorShadowTransform = shadowQuad.transform;
            }

            ApplySceneShadowDistance();

            GameObject keyGo = GameObject.Find("Key Light");
            if (keyGo != null) {
                keyLight = keyGo.GetComponent<Light>();
            }
            GameObject fillGo = GameObject.Find("Fill Light");
            if (fillGo != null) {
                fillLight = fillGo.GetComponent<Light>();
            }
            GameObject rimGo = GameObject.Find("Rim Light");
            if (rimGo != null) {
                rimLight = rimGo.GetComponent<Light>();
            }
            GameObject stageGo = GameObject.Find("StageLight");
            if (stageGo != null) {
                stageLight = stageGo.GetComponent<Light>();
            }
            GameObject probeGo = GameObject.Find("ShowroomReflectionProbe");
            if (probeGo != null) {
                showroomProbe = probeGo.GetComponent<ReflectionProbe>();
            }

            ConfigurePostProcessing();

            EnsureQueue(LoadRadius + 12);
            currentIndex = 0;
            itemTimer = 0.0f;
            ApplyText(items[queue[0]]);
            PlayPanelSequence();
            EnsureWindow(true);
            LayoutSlots();

            Canvas uiCanvas = panelGroup != null ? panelGroup.GetComponentInParent<Canvas>() : null;
            if (uiCanvas != null) {
                PresentationVersionLabel.Create(uiCanvas.rootCanvas, titleText);
            } else if (panelGroup != null) {
                Debug.LogError("[Presentation] No UI canvas above the info panel; the version label is not shown.");
            }

#if !UNITY_EDITOR
            // Browsing is an Editor tuning aid: a player is always the hands-off show, whatever the scene was saved with.
            debugBrowse = false;
#endif
            if (debugBrowse) {
                StartDebugBrowse();
            }
        }

        private void Update() {
            float dt = Time.deltaTime;
            itemTimer += dt;

            if (debugBrowse) {
                // Browse mode: the model stays until the next or previous button is used; its show keeps looping.
                if (itemTimer >= ShowSeconds) {
                    itemTimer = 0.0f;
                }
            } else if (autoAdvance && itemTimer >= ShowSeconds) {
                Advance();
            }

            RequestRefitIfScaleChanged();
            RefitIfRequested();

            EnsureWindow(false);
            LayoutSlots();
            AnimateSlots(dt);

            // Outside pop windows: active holders with scale > 0.01f must be exactly 1
            if (!debugBrowse && itemTimer > (PopInSeconds + 0.15f) && itemTimer < (ShowSeconds - PopOutSeconds - 0.15f)) {
                int activeHolderCount = 0;
                foreach (KeyValuePair<int, Slot> pair in slots) {
                    if (pair.Value.holder != null && pair.Value.holder.gameObject.activeInHierarchy && pair.Value.holder.localScale.x > 0.01f) {
                        activeHolderCount++;
                    }
                }
                if (activeHolderCount != 1) {
                    Debug.LogError("[Presentation Guard] Assertion failed: active holders with scale > 0.01 is " + activeHolderCount + " (expected exactly 1 outside pop window) at t=" + itemTimer.ToString("F2") + "s.");
                }
            }
        }

        /// <summary>One model at a time: the next one pops in the moment the last has popped out.</summary>
        private void Advance() {
            currentIndex = currentIndex + 1;
            EnsureQueue(currentIndex + LoadRadius + 12);
            itemTimer = 0.0f;
            ApplyText(items[queue[currentIndex]]);
            PlayPanelSequence();
        }

        /// <summary>The info panel fades in a moment after the model pops in and out as it pops out (DOTween, linear).</summary>
        private void PlayPanelSequence() {
            if (panelGroup == null) {
                return;
            }
            if (panelSequence != null) {
                panelSequence.Kill(false);
            }
            DOTween.Kill(panelGroup, false);
            panelGroup.alpha = 0.0f;
            float hold = ShowSeconds - PopOutSeconds - PanelDelaySeconds - PanelFadeSeconds;
            panelSequence = DOTween.Sequence();
            panelSequence.SetLink(panelGroup.gameObject, LinkBehaviour.KillOnDestroy);
            panelSequence.AppendInterval(PanelDelaySeconds);
            panelSequence.Append(panelGroup.DOFade(1.0f, PanelFadeSeconds).SetEase(Ease.Linear));
            panelSequence.AppendInterval(Mathf.Max(0.0f, hold));
            panelSequence.Append(panelGroup.DOFade(0.0f, PopOutSeconds).SetEase(Ease.Linear));
        }

        /// <summary>Linear pop-in (0 to 1), hold, linear pop-out (1 to 0) of the model on the stage (DOTween, owner order).</summary>
        private void PlayPopSequence(Slot slot) {
            if (slot.pop != null) {
                slot.pop.Kill(false);
            }
            if (slot.holder != null) {
                DOTween.Kill(slot.holder, false);
                slot.holder.localScale = Vector3.zero;
                slot.pop = DOTween.Sequence();
                slot.pop.SetLink(slot.holder.gameObject, LinkBehaviour.KillOnDestroy);
                slot.pop.Append(slot.holder.DOScale(Vector3.one, PopInSeconds).SetEase(Ease.Linear));
                slot.pop.AppendInterval(Mathf.Max(0.0f, ShowSeconds - PopInSeconds - PopOutSeconds));
                slot.pop.Append(slot.holder.DOScale(Vector3.zero, PopOutSeconds).SetEase(Ease.Linear));
                slot.popStarted = true;
            }
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

        private void OnDestroy() {
            if (panelSequence != null) {
                panelSequence.Kill();
            }
            RestoreSceneShadowDistance();
        }

        // The URP asset is shared with the other launcher scenes, so the shadow reach this scene needs is set while it runs and
        // the asset's own value is put back when it ends.
        private void ApplySceneShadowDistance() {
            shadowAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (shadowAsset == null) {
                return;
            }
            Camera viewCamera = ResolveFitCamera();
            Vector3 target = modelPlacement != null ? modelPlacement.position : (carouselRoot != null ? carouselRoot.position : transform.position);
            float scale = StereoRigPresent() ? XRRig.ViewScale : 1.0f;
            float needed = viewCamera != null ? Vector3.Distance(viewCamera.transform.position, target) + SceneShadowMargin * scale : SceneShadowMargin * 4.0f * scale;
            originalShadowDistance = shadowAsset.shadowDistance;
            // The shadow map should cover the stage and nothing more: models are a few tens of centimetres in the display's own space.
            if (!Mathf.Approximately(needed, originalShadowDistance)) {
                shadowAsset.shadowDistance = needed;
                shadowDistanceChanged = true;
            }
        }

        private void RestoreSceneShadowDistance() {
            if (shadowDistanceChanged && shadowAsset != null) {
                shadowAsset.shadowDistance = originalShadowDistance;
                shadowDistanceChanged = false;
            }
        }

        /// <summary>Fits the shown model again, without reloading it, when a refit was requested (the first browsed model, once the rig has settled).</summary>
        private void RefitIfRequested() {
            if (!slots.ContainsKey(currentIndex)) {
                return;
            }
            Slot slot = slots[currentIndex];
            PresentationItem item = slot.item;
            if (!slot.refitRequested) {
                return;
            }
            slot.refitRequested = false;
            if (slot.parts.Count > 0) {
                ApplyExplode(slot, 0.0f);
            }
            Quaternion spin = slot.spinner.localRotation;
            // The fit measures in world space, so the pop (holder scale, mid-tween) and the explode draw-back (spinner scale) are set
            // aside while it measures; otherwise a refit during the pop-in fits the model too large.
            Vector3 holderScale = slot.holder.localScale;
            Vector3 spinnerScale = slot.spinner.localScale;
            slot.holder.localScale = Vector3.one;
            slot.spinner.localScale = Vector3.one;
            slot.spinner.localRotation = Quaternion.identity;
            slot.model.localScale = Vector3.one;
            slot.model.localPosition = Vector3.zero;
            slot.model.localRotation = Quaternion.Euler(item.baseEuler);
            FitModel(slot, item);
            slot.baseScale = slot.model.localScale;
            slot.holder.localRotation = Quaternion.Euler(item.tiltDegrees, 0.0f, 0.0f);
            slot.spinner.localRotation = spin;
            slot.spinner.localScale = spinnerScale;
            slot.holder.localScale = holderScale;
            fitVersion++;
            LogBrowse();
        }

        /// <summary>A manual scale moved in the inspector: the model on stage is fitted again at the new size.</summary>
        private void RequestRefitIfScaleChanged() {
            if (!slots.ContainsKey(currentIndex)) {
                return;
            }
            Slot slot = slots[currentIndex];
            if (!Mathf.Approximately(slot.appliedScale, ManualScale(slot.item))) {
                slot.refitRequested = true;
            }
        }

        private static float ManualScale(PresentationItem item) {
            return Mathf.Clamp(item.scale, PresentationTuning.MinManualScale, PresentationTuning.MaxManualScale);
        }

        /// <summary>Copies the saved manual scales from the tuning asset into the items, by title.</summary>
        private void LoadSavedScales() {
            if (tuning == null) {
                return;
            }
            int loaded = 0;
            for (int i = 0; i < items.Count; i++) {
                float saved;
                if (tuning.TryGetScale(items[i].title, out saved)) {
                    items[i].scale = saved;
                    loaded++;
                }
            }
            if (loaded > 0) {
                Debug.Log("[Presentation] Loaded " + loaded + " saved model scales from " + tuning.name + ".");
            }
        }

        /// <summary>
        /// Writes every item's manual scale into the tuning asset in memory (a scale of 1 clears the model's entry). The editor saves the
        /// asset to disk; works in Play, so values tuned while playing are kept. Returns how many models have a scale other than 1.
        /// </summary>
        public int StoreScalesInTuning() {
            if (tuning == null) {
                Debug.LogError("[Presentation] No tuning asset is assigned to the director; the scales cannot be saved.", this);
                return 0;
            }
            int stored = 0;
            for (int i = 0; i < items.Count; i++) {
                float value = ManualScale(items[i]);
                tuning.SetScale(items[i].title, value);
                if (!Mathf.Approximately(value, 1.0f)) {
                    stored++;
                }
            }
            return stored;
        }

        /// <summary>Index in the item list of the model shown in browse mode (0 to ItemCount - 1, the same order as the Items list).</summary>
        public int BrowseIndex {
            get {
                return browseIndex;
            }
        }

        private void StartDebugBrowse() {
            if (panelGroup == null) {
                Debug.LogError("[Presentation] Browse buttons need the info panel canvas; none is assigned.");
                debugBrowse = false;
                return;
            }
            Canvas canvas = panelGroup.GetComponentInParent<Canvas>();
            if (canvas == null) {
                Debug.LogError("[Presentation] Browse buttons need a canvas above the info panel; none found.");
                debugBrowse = false;
                return;
            }
            PresentationDebugBrowser browser = gameObject.AddComponent<PresentationDebugBrowser>();
            browser.Build(this, canvas.rootCanvas, titleText, ResolveFitCamera());
            debugBrowser = browser;
            BrowseTo(0);
        }

        /// <summary>Shows the model at the given item index, wrapping at both ends (last then first, first then last).</summary>
        public void BrowseOffset(int delta) {
            int count = items.Count;
            BrowseTo(((browseIndex + delta) % count + count) % count);
        }

        public void BrowseTo(int index) {
            browseIndex = index;
            if (debugBrowser != null) {
                debugBrowser.Refresh();
            }
            if (browseTween != null) {
                browseTween.Kill(false);
                browseTween = null;
            }

            // The model on stage pops out (linear, DOTween), then the new one is built and pops in, the same as the automatic show.
            Transform leaving = GetCentreHolder();
            if (leaving != null && leaving.gameObject.activeInHierarchy && leaving.localScale.x > 0.01f) {
                DOTween.Kill(leaving, false);
                browseTween = leaving.DOScale(Vector3.zero, PopOutSeconds).SetEase(Ease.Linear).SetLink(leaving.gameObject, LinkBehaviour.KillOnDestroy);
                browseTween.OnComplete(delegate {
                    browseTween = null;
                    ShowBrowseModel(index);
                });
            } else {
                ShowBrowseModel(index);
            }
        }

        private void ShowBrowseModel(int index) {
            JumpToItem(index);
            itemTimer = 0.0f;
            Transform entering = GetCentreHolder();
            if (entering != null) {
                entering.localScale = Vector3.zero;
                browseTween = entering.DOScale(Vector3.one, PopInSeconds).SetEase(Ease.Linear).SetLink(entering.gameObject, LinkBehaviour.KillOnDestroy);
                browseTween.OnComplete(delegate {
                    browseTween = null;
                    // The very first model is fitted while the stereo rig is still settling in the player; fit it once more when it has
                    // popped in, so it can never stay at a size measured before the rig was ready.
                    if (!firstBrowseRefitDone && slots.ContainsKey(currentIndex)) {
                        firstBrowseRefitDone = true;
                        slots[currentIndex].refitRequested = true;
                    }
                });
            }
            LogBrowse();
        }

        private void LogBrowse() {
            Transform model = GetCentreModel();
            Transform holder = GetCentreHolder();
            if (model == null || holder == null) {
                return;
            }
            bool found;
            Bounds bounds = MeshBounds(model, out found);
            Debug.Log("[Presentation Browse] " + browseIndex + " / " + items.Count + "  " + items[browseIndex].title
                + "  modelLocalScale=" + model.localScale.x.ToString("F4")
                + "  worldSize=" + (found ? bounds.size.ToString("F2") : "none"));
        }

        public int ItemCount {
            get {
                return items.Count;
            }
        }

        public PresentationItem GetItem(int index) {
            if (index < 0 || index >= items.Count) {
                return null;
            }
            return items[index];
        }

        public void JumpToItem(int itemIndex) {
            if (itemIndex < 0 || itemIndex >= items.Count) {
                return;
            }
            if (panelSequence != null) {
                panelSequence.Kill();
            }
            foreach (KeyValuePair<int, Slot> pair in slots) {
                if (pair.Value.pop != null) {
                    pair.Value.pop.Kill();
                }
            }
            slots.Clear();
            if (carouselRoot != null) {
                for (int i = carouselRoot.childCount - 1; i >= 0; i--) {
                    GameObject childGo = carouselRoot.GetChild(i).gameObject;
                    childGo.SetActive(false);
                    Destroy(childGo);
                }
            }
            queue.Clear();
            queue.Add(itemIndex);
            EnsureQueue(LoadRadius + 12);
            currentIndex = 0;
            itemTimer = 0.0f;
            ApplyText(items[itemIndex]);
            SetPanelAlpha(1.0f);
            EnsureWindow(true);
            LayoutSlots();
            if (slots.ContainsKey(0)) {
                Slot slot = slots[0];
                slot.holder.localScale = Vector3.one;
                if (slot.pop != null) {
                    slot.pop.Kill();
                }
            }
        }

        public Transform GetCentreHolder() {
            if (slots.ContainsKey(currentIndex)) {
                return slots[currentIndex].holder;
            }
            return null;
        }

        public Transform GetCentreModel() {
            if (slots.ContainsKey(currentIndex)) {
                return slots[currentIndex].model;
            }
            return null;
        }

        public Transform GetCentreSpinner() {
            if (slots.ContainsKey(currentIndex)) {
                return slots[currentIndex].spinner;
            }
            return null;
        }

        /// <summary>The item of the model on stage (the same object as its entry in the Items list), or null.</summary>
        public PresentationItem GetCentreItem() {
            if (slots.ContainsKey(currentIndex)) {
                return slots[currentIndex].item;
            }
            return null;
        }

        /// <summary>Title of the model on stage, or an empty string.</summary>
        public string GetCentreTitle() {
            if (slots.ContainsKey(currentIndex)) {
                return slots[currentIndex].item.title;
            }
            return string.Empty;
        }

        /// <summary>
        /// Size of the model on stage at its fitted scale, measured in its spinner's space so the pop scale of the holder does not enter
        /// it: <paramref name="turnRadius"/> is the furthest any mesh corner reaches from the turning axis (what the model sweeps as it
        /// turns), <paramref name="halfHeight"/> the furthest above or below its centre. False when nothing is on stage.
        /// </summary>
        public bool TryGetCentreExtents(out float turnRadius, out float halfHeight) {
            turnRadius = 0.0f;
            halfHeight = 0.0f;
            if (!slots.ContainsKey(currentIndex)) {
                return false;
            }

            Slot slot = slots[currentIndex];
            Transform spinner = slot.spinner;
            // The size is that of the assembled model: parts that are spread apart by the explode show are put back for the measurement
            // and set to where the show has them afterwards, so a placement made mid-explode does not push the model back.
            bool exploded = slot.parts.Count > 0;
            if (exploded) {
                ApplyExplode(slot, 0.0f);
            }

            Renderer[] renderers = spinner.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++) {
                Bounds local;
                if (!TryGetLocalMeshBounds(renderers[i], out local)) {
                    continue;
                }

                Matrix4x4 toSpinner = RelativeMatrix(renderers[i].transform, spinner);
                Vector3 min = local.min;
                Vector3 max = local.max;
                for (int c = 0; c < 8; c++) {
                    Vector3 corner = new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y, (c & 4) == 0 ? min.z : max.z);
                    Vector3 point = toSpinner.MultiplyPoint3x4(corner);
                    turnRadius = Mathf.Max(turnRadius, Mathf.Sqrt(point.x * point.x + point.z * point.z));
                    halfHeight = Mathf.Max(halfHeight, Mathf.Abs(point.y));
                    found = true;
                }
            }

            if (exploded) {
                ApplyExplode(slot, ExplodeCurve(itemTimer));
            }

            return found;
        }

        private static bool TryGetLocalMeshBounds(Renderer renderer, out Bounds bounds) {
            bounds = new Bounds();
            if (renderer.name.Contains("Halo") || renderer.name.Contains("Billboard")) {
                return false;
            }

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null) {
                if (skinned.sharedMesh == null) {
                    return false;
                }
                bounds = skinned.sharedMesh.bounds;
                return true;
            }

            if (!(renderer is MeshRenderer)) {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) {
                return false;
            }

            bounds = filter.sharedMesh.bounds;
            return true;
        }

        // Product of the local transforms from child up to (not including) ancestor; never inverts a matrix, so a holder at scale 0
        // during its pop does not matter.
        private static Matrix4x4 RelativeMatrix(Transform child, Transform ancestor) {
            Matrix4x4 matrix = Matrix4x4.identity;
            Transform current = child;
            while (current != null && current != ancestor) {
                matrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * matrix;
                current = current.parent;
            }
            return matrix;
        }

        public Transform GetFloorShadow() {
            return floorShadowTransform;
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
                descriptionText.text = string.IsNullOrEmpty(item.shortDescription) ? item.description : item.shortDescription;
            }
            if (perItemLighting) {
                ApplyPerItemLighting(item);
            }
        }

        private void ApplyPerItemLighting(PresentationItem item) {
            if (item == null) {
                return;
            }
            if (item.title == "Volvo S90" || item.show == PresentationShow.EngineRun) {
                if (fillLight != null) {
                    fillLight.intensity = 0.98f;
                }
                if (rimLight != null) {
                    rimLight.intensity = 1.88f;
                }
                if (keyLight != null) {
                    keyLight.intensity = 1.88f;
                }
            } else if (item.title.Contains("Coffee Table") || item.title.Contains("Dining Table") || item.title.Contains("Armchair")) {
                if (fillLight != null) {
                    fillLight.intensity = 0.68f;
                }
                if (keyLight != null) {
                    keyLight.intensity = 1.58f;
                }
                if (rimLight != null) {
                    rimLight.intensity = 1.50f;
                }
            } else {
                if (fillLight != null) {
                    fillLight.intensity = 0.75f;
                }
                if (keyLight != null) {
                    keyLight.intensity = 1.80f;
                }
                if (rimLight != null) {
                    rimLight.intensity = 1.65f;
                }
            }
            if (showroomProbe != null) {
                showroomProbe.RenderProbe();
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
                if (pair.Key < currentIndex || pair.Key > currentIndex + LoadRadius + 1) {
                    remove.Add(pair.Key);
                }
            }
            for (int i = 0; i < remove.Count; i++) {
                int removeKey = remove[i];
                if (slots.ContainsKey(removeKey)) {
                    Slot oldSlot = slots[removeKey];
                    if (oldSlot.pop != null) {
                        oldSlot.pop.Kill(false);
                        oldSlot.pop = null;
                    }
                    if (oldSlot.holder != null) {
                        DOTween.Kill(oldSlot.holder, false);
                        oldSlot.holder.gameObject.SetActive(false);
                        Destroy(oldSlot.holder.gameObject);
                    }
                    slots.Remove(removeKey);
                }
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

            FitModel(slot, item);
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
            CalibrateMaterials(model);
            if (item.group == "Jewellery") {
                SoftenJewelleryShine(model);
            }
            if (item.title == "Solar System") {
                MakeOrbitPathsVisible(model);
            }
            if (anatomy) {
                AttachAnatomyBehaviour(model, item.anatomyTopic);
            }
            holderGo.transform.localRotation = Quaternion.Euler(item.tiltDegrees, 0.0f, 0.0f);
            holderGo.SetActive(false);
            return slot;
        }

        private void ConfigurePostProcessing() {
            GameObject postFxGo = GameObject.Find("PostFX");
            if (postFxGo == null) {
                return;
            }
            UnityEngine.Rendering.Volume vol = postFxGo.GetComponent<UnityEngine.Rendering.Volume>();
            if (vol == null) {
                return;
            }
            UnityEngine.Rendering.VolumeProfile profile = vol.sharedProfile != null ? vol.sharedProfile : vol.profile;
            if (profile == null) {
                return;
            }
            UnityEngine.Rendering.Universal.ColorAdjustments colorAdj;
            if (profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out colorAdj)) {
                colorAdj.postExposure.overrideState = true;
                colorAdj.postExposure.value = -0.15f;
            }
        }

        private static void BindPcbExhibit(Slot slot) {
            Component exhibit = slot.model.GetComponent("PcbWorkingExhibit");
            if (exhibit == null) {
                Type type = FindType("ViitorCloud.KMaxPCBAssembly.PcbWorkingExhibit");
                if (type != null) {
                    exhibit = slot.model.gameObject.AddComponent(type);
                }
            }
            // Owner, 2026-11-26: the presentation shows the board with its LED breathing, without the current-flow sparks and glow.
            PropertyInfo showFlow = exhibit != null ? exhibit.GetType().GetProperty("ShowCurrentFlow") : null;
            if (showFlow != null) {
                showFlow.SetValue(exhibit, false, null);
            } else if (exhibit != null) {
                Debug.LogWarning("[Presentation] PcbWorkingExhibit has no ShowCurrentFlow; the PCB current flow stays visible.");
            }
            Transform[] children = slot.model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++) {
                if (children[i] != null && children[i].name.StartsWith("LedHalo")) {
                    children[i].gameObject.SetActive(false);
                    Destroy(children[i].gameObject);
                }
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
        private static readonly int RoughnessFactorId = Shader.PropertyToID("roughnessFactor");
        private static readonly int MetallicFactorId = Shader.PropertyToID("metallicFactor");
        /// <summary>Lowest roughness a jewellery material keeps (about 0.17 as shipped; 0.42 still glared in the showcase), so metal reads as satin and not as a mirror.</summary>
        private const float JewelleryMinRoughness = 0.55f;
        private const float JewelleryMaxMetallic = 0.80f;
        /// <summary>Width, in model units, of a planet's path in the Solar System (it was 0.0012).</summary>
        private const float OrbitPathWidth = 0.0045f;
        /// <summary>Owner, 2026-11-26: every planet path is dark grey.</summary>
        private static readonly Color OrbitPathColour = new Color(0.36f, 0.37f, 0.40f, 1.0f);

        /// <summary>Every surface draws both faces, so a cut-away or an open shell never shows its back as a hole.</summary>
        /// <summary>
        /// Owner, 2026-11-26: the jewellery shone too much under the showcase lights. Metal reflects the whole studio, so a mirror-smooth
        /// metal (the packs ship gold and silver at a roughness of about 0.17) turns into a white glare. The roughness is raised to a satin
        /// finish (glTF materials: roughnessFactor; URP Lit materials: smoothness), which keeps the colour and the sparkle of the stones.
        /// </summary>
        private static void SoftenJewelleryShine(GameObject model) {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) {
                    continue;
                }

                Material[] materials = renderers[i].materials;
                for (int m = 0; m < materials.Length; m++) {
                    Material mat = materials[m];
                    if (mat == null) {
                        continue;
                    }

                    if (mat.HasProperty(RoughnessFactorId)) {
                        mat.SetFloat(RoughnessFactorId, Mathf.Max(mat.GetFloat(RoughnessFactorId), JewelleryMinRoughness));
                    }
                    if (mat.HasProperty(MetallicFactorId)) {
                        mat.SetFloat(MetallicFactorId, Mathf.Min(mat.GetFloat(MetallicFactorId), JewelleryMaxMetallic));
                    }
                    if (mat.HasProperty("_Smoothness")) {
                        mat.SetFloat("_Smoothness", Mathf.Min(mat.GetFloat("_Smoothness"), 1.0f - JewelleryMinRoughness));
                    }
                }
            }
        }

        /// <summary>
        /// Owner, 2026-11-26: show the path of each planet. The preview prefab already has a ring per planet, but a hair-thin, dim blue
        /// one that is lost against the stars. Each ring is made wider and solid dark grey (owner's choice), so every path reads at a glance.
        /// </summary>
        private static void MakeOrbitPathsVisible(GameObject model) {
            Color pathColour = OrbitPathColour;
            string[] planets = new string[] { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
            LineRenderer[] lines = model.GetComponentsInChildren<LineRenderer>(true);
            int widened = 0;
            for (int i = 0; i < lines.Length; i++) {
                for (int p = 0; p < planets.Length; p++) {
                    if (lines[i].name.StartsWith(planets[p], StringComparison.OrdinalIgnoreCase)) {
                        lines[i].widthMultiplier = OrbitPathWidth;
                        lines[i].startColor = pathColour;
                        lines[i].endColor = pathColour;
                        // The ring material carries a dim blue tint (alpha 0.4) that multiplies the line colour; this copy of it is
                        // neutral, so the path shows in its own colour.
                        Material pathMaterial = lines[i].material;
                        if (pathMaterial != null) {
                            if (pathMaterial.HasProperty("_BaseColor")) {
                                pathMaterial.SetColor("_BaseColor", Color.white);
                            }
                            if (pathMaterial.HasProperty("_Color")) {
                                pathMaterial.SetColor("_Color", Color.white);
                            }
                        }
                        widened++;
                        break;
                    }
                }
            }

            if (widened == 0) {
                Debug.LogWarning("[Presentation] The Solar System has no planet path lines to make visible.");
            }
        }

        private static void MakeDoubleSided(GameObject model) {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) {
                    continue;
                }
                if (renderers[i].name.Contains("Halo") || renderers[i].name.Contains("Billboard")) {
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

        /// <summary>Prevents blown-out white surfaces and excessive specular glare on table tops and retail furniture, while preserving glossy automotive glass/paint and preventing crushed black cabin interiors.</summary>
        private static void CalibrateMaterials(GameObject model) {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) {
                    continue;
                }
                if (renderers[i].name.Contains("Halo") || renderers[i].name.Contains("Billboard")) {
                    continue;
                }
                Material[] materials = renderers[i].materials;
                for (int m = 0; m < materials.Length; m++) {
                    Material mat = materials[m];
                    if (mat == null) {
                        continue;
                    }

                    string matName = mat.name;
                    bool isGlass = matName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matName.IndexOf("Lens", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matName.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matName.IndexOf("windo", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isAutomotive = matName.IndexOf("Paint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       matName.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       matName.IndexOf("Rim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       matName.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isInterior = matName.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isGlass) {
                        if (mat.HasProperty("_Smoothness")) {
                            mat.SetFloat("_Smoothness", 0.95f);
                        }
                        if (mat.HasProperty("_BaseColor")) {
                            Color col = mat.GetColor("_BaseColor");
                            if (col.r < 0.08f && col.g < 0.08f && col.b < 0.08f) {
                                mat.SetColor("_BaseColor", new Color(0.10f, 0.12f, 0.15f, Mathf.Max(col.a, 0.35f)));
                            }
                        }
                        continue;
                    }

                    if (isInterior) {
                        if (mat.HasProperty("_BaseColor")) {
                            Color col = mat.GetColor("_BaseColor");
                            if (col.r < 0.06f && col.g < 0.06f && col.b < 0.06f) {
                                mat.SetColor("_BaseColor", new Color(0.11f, 0.11f, 0.13f, col.a));
                            }
                        }
                        continue;
                    }

                    bool isWhiteSurface = false;
                    if (mat.HasProperty("_BaseColor")) {
                        Color col = mat.GetColor("_BaseColor");
                        float lum = 0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b;
                        if (lum >= 0.70f || (col.r >= 0.70f && col.g >= 0.70f && col.b >= 0.70f)) {
                            isWhiteSurface = true;
                            float maxComp = Mathf.Max(col.r, Mathf.Max(col.g, col.b));
                            float scale = maxComp > 0.62f ? (0.62f / maxComp) : 1.0f;
                            mat.SetColor("_BaseColor", new Color(col.r * scale, col.g * scale, col.b * scale, col.a));
                        }
                    }
                    if (mat.HasProperty("_Color")) {
                        Color col = mat.GetColor("_Color");
                        float lum = 0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b;
                        if (lum >= 0.70f || (col.r >= 0.70f && col.g >= 0.70f && col.b >= 0.70f)) {
                            isWhiteSurface = true;
                            float maxComp = Mathf.Max(col.r, Mathf.Max(col.g, col.b));
                            float scale = maxComp > 0.62f ? (0.62f / maxComp) : 1.0f;
                            mat.SetColor("_Color", new Color(col.r * scale, col.g * scale, col.b * scale, col.a));
                        }
                    }
                    float maxSmooth = isWhiteSurface ? 0.08f : (isAutomotive ? 0.85f : 0.22f);
                    if (mat.HasProperty("_Smoothness")) {
                        float smoothness = mat.GetFloat("_Smoothness");
                        if (smoothness > maxSmooth) {
                            mat.SetFloat("_Smoothness", maxSmooth);
                        }
                    }
                    if (mat.HasProperty("_Glossiness")) {
                        float gloss = mat.GetFloat("_Glossiness");
                        if (gloss > maxSmooth) {
                            mat.SetFloat("_Glossiness", maxSmooth);
                        }
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
                if (renderers[i].name.Contains("Halo") || renderers[i].name.Contains("Billboard")) {
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

        private void FitModel(Slot slot, PresentationItem item) {
            float fitSize = item.fitSize;
            slot.holder.localPosition = HolderOrigin(item);
            slot.holder.localRotation = Quaternion.identity;
            slot.holder.localScale = Vector3.one;

            bool found;
            Bounds bounds = MeshBounds(slot.model, out found);
            if (!found) {
                Debug.LogWarning("[Presentation] " + slot.model.name + " has no mesh renderers; left unscaled.");
                return;
            }

            float scale;
            Camera viewCamera = ResolveFitCamera();
            if (StereoRigPresent()) {
                scale = StereoFitScale(bounds, HeightFill(item));
            } else if (viewCamera != null) {
                scale = ScreenFitScale(viewCamera, slot.holder.position, bounds, HeightFill(item));
            } else {
                // fitSize is the tallest the model may stand; the footprint cap keeps a wide model (a car, a bed, a biplane) inside the frame as it turns.
                float footprint = Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z);
                float byHeight = bounds.size.y > 0.0001f ? fitSize / bounds.size.y : float.MaxValue;
                float byFootprint = footprint > 0.0001f ? maxFootprint / footprint : float.MaxValue;
                scale = Mathf.Min(byHeight, byFootprint);
                if (scale > 100000.0f) {
                    scale = 1.0f;
                }
                scale = scale * 0.90f;
            }
            slot.appliedScale = ManualScale(item);
            slot.model.localScale = slot.model.localScale * (scale * slot.appliedScale);

            Bounds scaled = MeshBounds(slot.model, out found);
            Vector3 centreLocal;
            Transform sunChild = FindChildByName(slot.model, "Sun");
            if (sunChild != null) {
                bool sunFound;
                Bounds sunBounds = MeshBounds(sunChild, out sunFound);
                if (sunFound) {
                    centreLocal = slot.spinner.InverseTransformPoint(sunBounds.center);
                } else {
                    centreLocal = slot.spinner.InverseTransformPoint(sunChild.position);
                }
            } else {
                centreLocal = slot.spinner.InverseTransformPoint(scaled.center);
            }
            slot.model.localPosition -= centreLocal;
            slot.footprint = new Vector2(Mathf.Max(0.50f, scaled.size.x), Mathf.Max(0.50f, scaled.size.z));
        }

        private void ResolvePlacement() {
            if (shadowFloor == null) {
                Transform[] floorMarkers = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < floorMarkers.Length; i++) {
                    if (floorMarkers[i].name == "ModelFloor") {
                        shadowFloor = floorMarkers[i];
                        break;
                    }
                }
            }
            if (modelPlacement == null) {
                // The marker may be switched off in the scene (it is only a position), so inactive objects are searched too.
                Transform[] everything = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < everything.Length; i++) {
                    if (everything[i].name == "ModelPlacement") {
                        modelPlacement = everything[i];
                        break;
                    }
                }
            }
            if (modelPlacement == null) {
                // The Carousel origin is the stage when there is no ModelPlacement marker.
                return;
            }
            Renderer[] markers = modelPlacement.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < markers.Length; i++) {
                markers[i].enabled = false;
            }
            Collider[] markerColliders = modelPlacement.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < markerColliders.Length; i++) {
                markerColliders[i].enabled = false;
            }
        }

        // True when the scene has the Kmax rig: models then live in the display's own space, inside its comfort volume.
        private bool StereoRigPresent() {
            if (!stereoFit) {
                return false;
            }
            if (stereoRig == null) {
                XRRig rig = FindFirstObjectByType<XRRig>();
                if (rig != null) {
                    stereoRig = rig.transform;
                }
            }
            return stereoRig != null;
        }

        private static float PopOutLimit() {
            return (StereoCamera.DefaultDistance - ComfortNearFromCamera) * XRRig.ViewScale;
        }

        private static float DepthLimit() {
            return (ComfortFarFromCamera - StereoCamera.DefaultDistance) * XRRig.ViewScale;
        }

        // Where a model turns on the view axis (positive is behind the glass). On the glass by default, as in the anatomy scene, so the
        // near half of it is in front of the screen.
        private float BudgetCentre() {
            return stageCentre * XRRig.ViewScale;
        }

        // Largest uniform factor that keeps the model, as it turns, inside the display's comfort volume and the stereo window:
        // its height inside the fraction of the glass height, its footprint circle inside the depth budget, and its width (magnified
        // by the projection from the eye when it comes forward) inside the glass width.
        private float StereoFitScale(Bounds bounds, float heightFill) {
            float viewScale = XRRig.ViewScale;
            Vector2 window = XRRig.ViewSize;
            float eye = StereoCamera.DefaultDistance * viewScale;
            float height = Mathf.Max(0.0001f, bounds.size.y);
            float diagonal = Mathf.Max(0.0001f, Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z));

            float byHeight = window.y * heightFill / height;
            float centre = BudgetCentre();
            // The turning footprint is a circle of radius diagonal / 2 around the centre: its front must stay inside the pop-out limit
            // and its back inside the depth limit.
            float margin = ComfortMargin * viewScale;
            float radiusCap = Mathf.Min(centre + PopOutLimit() - margin, DepthLimit() - margin - centre);
            float byBudget = Mathf.Max(0.01f * viewScale, radiusCap * 2.0f) / diagonal;

            float scale = Mathf.Min(byHeight, byBudget);
            for (int i = 0; i < 6; i++) {
                float radius = diagonal * scale * 0.5f;
                float frontDepth = centre - radius;
                float toEye = Mathf.Max(0.05f * viewScale, frontDepth + eye);
                float magnification = frontDepth < 0.0f ? eye / toEye : 1.0f;
                float byWidth = window.x * 0.5f * WindowFill / (magnification * diagonal * 0.5f);
                scale = Mathf.Min(byHeight, Mathf.Min(byBudget, byWidth));
            }
            return scale;
        }

        private Vector3 HolderOrigin(PresentationItem item) {
            if (anchorStageToOrigin && carouselRoot != null) {
                return Vector3.zero;
            }
            if (StereoRigPresent()) {
                Vector3 stage = stereoRig.position + stereoRig.forward * BudgetCentre();
                return carouselRoot != null ? carouselRoot.InverseTransformPoint(stage) : stage;
            }
            Vector3 world = modelPlacement != null ? modelPlacement.position : (carouselRoot != null ? carouselRoot.position : Vector3.zero);
            if (carouselRoot == null) {
                return world;
            }
            return carouselRoot.InverseTransformPoint(world);
        }

        private float HeightFill(PresentationItem item) {
            return fitHeightFraction;
        }

        private Camera ResolveFitCamera() {
            if (fitCamera != null) {
                return fitCamera;
            }
            Camera best = null;
            Camera[] everything = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < everything.Length; i++) {
                if (everything[i].name == "left" && everything[i].transform.root.name == "XRRig") {
                    best = everything[i];
                    break;
                }
            }
            for (int i = 0; best == null && i < everything.Length; i++) {
                if (everything[i].transform.root.name == "XRRig") {
                    best = everything[i];
                }
            }
            if (best == null) {
                best = Camera.main;
            }
            if (best != null) {
                fitCamera = best;
                Debug.Log("[Presentation] Fitting models to camera '" + best.name + "' (fov " + best.fieldOfView + ").");
            } else {
                Debug.LogWarning("[Presentation] No camera found to fit models to; using the fixed fit sizes.");
            }
            return best;
        }

        // Largest uniform factor that keeps the model, including its turn, inside the screen fill fractions at the placement distance.
        private float ScreenFitScale(Camera viewCamera, Vector3 centre, Bounds bounds, float heightFill) {
            float height = Mathf.Max(0.0001f, bounds.size.y);
            float diagonal = Mathf.Max(0.0001f, Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z));
            float depth = Mathf.Max(0.1f, Vector3.Dot(centre - viewCamera.transform.position, viewCamera.transform.forward));
            // The Kmax rig draws with its own projection matrix, so the frustum is read from the matrix, not from fieldOfView.
            Matrix4x4 projection = viewCamera.projectionMatrix;
            float tanVertical = Mathf.Abs(projection.m11) > 0.0001f ? 1.0f / Mathf.Abs(projection.m11) : Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHorizontal = Mathf.Abs(projection.m00) > 0.0001f ? 1.0f / Mathf.Abs(projection.m00) : tanVertical * 16.0f / 9.0f;
            float scale = 1.0f;
            for (int i = 0; i < 6; i++) {
                float frontDepth = Mathf.Max(0.1f, depth - diagonal * scale * 0.5f);
                float visibleHeight = 2.0f * frontDepth * tanVertical;
                float visibleWidth = 2.0f * frontDepth * tanHorizontal;
                float byHeight = visibleHeight * heightFill / height;
                float byWidth = visibleWidth * fitWidthFraction / diagonal;
                scale = Mathf.Min(byHeight, byWidth);
            }
            return scale;
        }

        private static Transform FindChildByName(Transform parent, string childName) {
            if (parent.name == childName) {
                return parent;
            }
            for (int i = 0; i < parent.childCount; i++) {
                Transform found = FindChildByName(parent.GetChild(i), childName);
                if (found != null) {
                    return found;
                }
            }
            return null;
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
                bool visible = pair.Key == currentIndex;
                if (slot.holder.gameObject.activeSelf != visible) {
                    slot.holder.gameObject.SetActive(visible);
                }
                if (!visible) {
                    slot.holder.localScale = Vector3.zero;
                    continue;
                }
                if (!slot.popStarted) {
                    PlayPopSequence(slot);
                }
                slot.holder.localPosition = HolderOrigin(slot.item);
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

            if (floorShadowTransform != null && floorShadowRenderer != null && slots.ContainsKey(currentIndex)) {
                Slot centreSlot = slots[currentIndex];
                float popScale = centreSlot.holder.localScale.x;
                if (popScale > 0.01f) {
                    if (!floorShadowRenderer.enabled) {
                        floorShadowRenderer.enabled = true;
                    }
                    bool found;
                    Bounds bounds = MeshBounds(centreSlot.model, out found);
                    float lowestY = found ? bounds.min.y : -0.695f;
                    float contactY = found ? (lowestY + 0.01f * Mathf.Max(1.0f, bounds.size.y)) : -0.695f;
                    float shadowAlpha = ShadowMaxAlpha;
                    if (shadowFloor != null && found) {
                        // A floating model drops its shadow onto the floor below it; the shadow gets fainter the higher the model hovers.
                        float gap = Mathf.Max(0.0f, lowestY - shadowFloor.position.y);
                        contactY = shadowFloor.position.y + 0.01f;
                        shadowAlpha = Mathf.Lerp(ShadowMaxAlpha, ShadowMaxAlpha * 0.35f, Mathf.Clamp01(gap / Mathf.Max(0.0001f, bounds.size.y)));
                    }
                    float posX = found ? bounds.center.x : centreSlot.holder.position.x;
                    float posZ = found ? bounds.center.z : centreSlot.holder.position.z;
                    // The shadow follows what the model covers on the floor right now, so it grows and shrinks with every model and as it turns.
                    float width = found ? Mathf.Max(0.05f, bounds.size.x * ShadowPadding) : Mathf.Max(1.20f, centreSlot.footprint.x * 1.50f) * popScale;
                    float length = found ? Mathf.Max(0.05f, bounds.size.z * ShadowPadding) : Mathf.Max(1.40f, centreSlot.footprint.y * 1.50f) * popScale;
                    floorShadowTransform.position = new Vector3(posX, contactY, posZ);
                    floorShadowTransform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                    floorShadowTransform.localScale = new Vector3(width, length, 1.0f);
                    if (shadowBlock == null) {
                        shadowBlock = new MaterialPropertyBlock();
                    }
                    floorShadowRenderer.GetPropertyBlock(shadowBlock);
                    shadowBlock.SetColor("_BaseColor", new Color(0.0f, 0.0f, 0.0f, shadowAlpha));
                    floorShadowRenderer.SetPropertyBlock(shadowBlock);
                } else {
                    if (floorShadowRenderer.enabled) {
                        floorShadowRenderer.enabled = false;
                    }
                }
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
            // The float is a few millimetres in the display's own space (it was 9 cm once the carousel scale was applied), one slow
            // sine with eased ends, the same for every model.
            if (isCentre) {
                float bob = Mathf.Sin(itemTimer * BobRadiansPerSecond) * BobMetres * (StereoRigPresent() ? XRRig.ViewScale : 1.0f);
                slot.holder.position = slot.holder.position + Vector3.up * bob;
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
