using UnityEngine;

using ViitorCloud.KmaxDisplay;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>
    /// The pen and mouse view standard of the anatomy exhibit for the presentation, and where each model stands in depth.
    ///
    /// <para>Input: the viewer orbits the rig around the model (button 0 or a mouse drag), the dolly input (button 2 pushed and pulled,
    /// the wheel, W and S) zooms the carousel about the model instead of moving the camera, and the reset gesture (button 1 tap, R) puts
    /// the zoom and the view back.</para>
    ///
    /// <para>Depth: the model always stands at the orbit origin at the size the director fitted it to; its scale is never changed for
    /// depth. Each time a new model comes on stage the rig flies to the home angle (the retail showroom's: level yaw, 14 degrees down)
    /// at the distance that brings the model as far out of the glass as it can comfortably come, for the strongest negative parallax:
    /// its nearest point, as it turns, no nearer than <see cref="nearestMargin"/> inside the display's pop-out limit; its width and
    /// height, enlarged by coming forward, inside the window; its far side inside the depth limit. The zoom starts at 1 for every
    /// model.</para>
    /// </summary>
    public class PresentationViewControl : MonoBehaviour, IViewResetHandler {
        private const float Settled = 0.0005f;
        private const float BudgetFit = 0.97f;

        [SerializeField, Tooltip("Source of the orbit and of the dolly input.")]
        private ViewerFlyController viewer;
        [SerializeField, Tooltip("Gives which model is on stage and its size.")]
        private PresentationDirector director;
        [SerializeField, Tooltip("Scaled about its origin, where the model stands, to zoom.")]
        private Transform content;
        [SerializeField, Tooltip("Keeps the model inside the depth budget and reports how deep it is. Optional.")]
        private ComfortDepthKeeper depthKeeper;
        [SerializeField, Tooltip("Largest zoom over the fitted size.")]
        private float maxZoom = 2.4f;
        [SerializeField, Tooltip("Natural-log units of zoom for each metre of dolly input. A wheel notch is about 0.05 m.")]
        private float dollySensitivity = 3f;
        [SerializeField, Tooltip("Seconds the zoom takes to settle.")]
        private float zoomSmoothTime = 0.16f;

        [Header("Home view and depth")]
        [SerializeField, Tooltip("Yaw of the home view, in degrees. Kept equal to the viewer's own home yaw by the scene builder.")]
        private float homeYaw;
        [SerializeField, Tooltip("Pitch of the home view, in degrees; positive looks down on the model (retail showroom: 14).")]
        private float homePitch = 14f;
        [SerializeField, Tooltip("Metres kept between the model's nearest point and the display's pop-out limit. 0.01 puts the nearest point 12 cm out, as the reference experiences do (cars 12.4 to 13.4 cm).")]
        private float nearestMargin = 0.01f;
        [SerializeField, Tooltip("Metres kept between the model's far side and the display's depth limit.")]
        private float farMargin = 0.015f;
        [SerializeField, Range(0.5f, 1f), Tooltip("Share of the window's width the model may cover, as seen from the eye.")]
        private float windowWidthFill = 0.9f;
        [SerializeField, Range(0.5f, 1f), Tooltip("Share of the window's height the model may cover, as seen from the eye.")]
        private float windowHeightFill = 0.8f;
        [SerializeField, Tooltip("Most the model's centre is brought out of the glass, in metres.")]
        private float maxPopOut = 0.11f;
        [SerializeField, Tooltip("Seconds the view takes to move to a new model's home.")]
        private float placeSeconds = 0.24f;

        [Header("Per-model tuning")]
        [SerializeField, Tooltip("Saved pop-out per model. A model with no entry is placed automatically.")]
        private PresentationTuning tuning;
        [SerializeField, Tooltip("Largest size a tuned model may reach, over the director's automatic fit.")]
        private float maxScale = 1.6f;
        [SerializeField, Tooltip("Smallest size a tuned model may shrink to, over the director's automatic fit.")]
        private float minScale = 0.3f;

        private Transform _tracked;
        private int _placedFitVersion = -1;
        private float _zoom = 1f;
        private float _zoomTarget = 1f;
        private float _zoomVelocity;
        private float _homeDistance;
        private float _scaleFactor = 1f;

        /// <summary>Metres the current model's centre stands in front of the glass at its home view.</summary>
        public float PopOut { get; private set; }

        /// <summary>Metres the current model's nearest point comes in front of the glass at its home view.</summary>
        public float NearestPoint { get; private set; }

        /// <summary>Which limit decided the current model's depth (automatic) or size (tuned): "pop-out", "width", "height", "max", "depth", "min" or "none".</summary>
        public string DepthLimiter { get; private set; }

        /// <summary>Size of the current model over the director's automatic fit: 1 when automatic, chosen from the pop-out when tuned.</summary>
        public float ScaleFactor {
            get {
                return _scaleFactor;
            }
        }

        /// <summary>Where the current model would stand if it were automatic, in metres in front of the glass.</summary>
        public float AutoPopOut { get; private set; }

        /// <summary>True when the current model's pop-out comes from the tuning asset.</summary>
        public bool IsTuned { get; private set; }

        /// <summary>Most a tuned model may be brought out, in metres.</summary>
        public float MaxPopOut {
            get {
                return maxPopOut;
            }
        }

        public PresentationTuning Tuning {
            get {
                return tuning;
            }
        }

        /// <summary>
        /// Tuning, Editor: places the current model at <paramref name="popOut"/> metres out with the largest size that fits there, and
        /// keeps that choice in the tuning asset in memory (saving the asset writes it to disk).
        /// </summary>
        public void ApplyPopOut(float popOut) {
            if (tuning == null) {
                Debug.LogWarning("[Presentation View] No tuning asset is assigned; the pop-out cannot be kept.", this);
                return;
            }

            string title = director.GetCentreTitle();
            tuning.SetPopOut(title, Mathf.Clamp(popOut, 0f, maxPopOut), 1f);
            PlaceCentreModel(false);
            tuning.SetPopOut(title, PopOut, _scaleFactor);
        }

        /// <summary>Tuning, Editor: returns the current model to automatic placement.</summary>
        public void UseAutomatic() {
            if (tuning != null) {
                tuning.ClearPopOut(director.GetCentreTitle());
            }

            PlaceCentreModel(false);
        }

        private void Awake() {
            if (viewer == null || director == null || content == null) {
                Debug.LogError($"{nameof(PresentationViewControl)} on '{name}' needs a viewer, a director and the content to zoom; zoom, reset and placement are off.", this);
                enabled = false;
                return;
            }

            _homeDistance = EyeDistance();
            DepthLimiter = string.Empty;
        }

        private void OnEnable() {
            if (viewer != null) {
                viewer.DollyInput += OnDollyInput;
                viewer.SetResetHandler(this);
            }
        }

        private void OnDisable() {
            if (viewer != null) {
                viewer.DollyInput -= OnDollyInput;
            }
        }

        public void ResetToHome() {
            _zoomTarget = 1f;
            viewer.FlyTo(homeYaw, homePitch, _homeDistance, viewer.EnableFly);
        }

        private void Update() {
            TrackCentreModel();
            LimitToBudget();
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomVelocity, zoomSmoothTime);
            content.localScale = Vector3.one * (_zoom * _scaleFactor);
        }

        private void TrackCentreModel() {
            Transform centre = director.GetCentreHolder();
            // The same model fitted again at a new manual scale is placed again; a new model also starts at zoom 1.
            bool refitted = centre != null && centre == _tracked && director.FitVersion != _placedFitVersion;
            if (!refitted && (centre == _tracked || (centre != null && !centre.gameObject.activeInHierarchy))) {
                return;
            }

            _placedFitVersion = director.FitVersion;
            if (!refitted) {
                _tracked = centre;
                _zoom = 1f;
                _zoomTarget = 1f;
                _zoomVelocity = 0f;
                _scaleFactor = 1f;
                content.localScale = Vector3.one;
            }

            if (depthKeeper != null) {
                depthKeeper.Track(centre);
            }

            if (centre != null) {
                PlaceCentreModel(viewer.EnableFly);
            }
        }

        /// <summary>
        /// Chooses the depth and size of the model on stage and moves the rig there; see the class summary for the rules. Automatic: the
        /// size is the director's fit and the depth is as far out as it allows. Tuned: the depth is the saved pop-out and the size is the
        /// largest that fits there, between <see cref="minScale"/> and <see cref="maxScale"/>.
        /// </summary>
        private void PlaceCentreModel(bool animated) {
            float radius;
            float halfHeight;
            if (!director.TryGetCentreExtents(out radius, out halfHeight)) {
                Debug.LogWarning($"[Presentation View] '{director.GetCentreTitle()}' has no measurable mesh; it stays on the glass.", this);
                PopOut = 0f;
                AutoPopOut = 0f;
                NearestPoint = 0f;
                DepthLimiter = "none";
                IsTuned = false;
                _scaleFactor = 1f;
                _homeDistance = EyeDistance();
                viewer.FlyTo(homeYaw, homePitch, _homeDistance, animated, placeSeconds);
                return;
            }

            float eye = EyeDistance();
            float pitch = Mathf.Abs(homePitch) * Mathf.Deg2Rad;
            // What the turning model covers seen at the home pitch: in depth (towards the viewer) and on screen vertically.
            float depthReach = radius * Mathf.Cos(pitch) + halfHeight * Mathf.Sin(pitch);
            float screenHalfHeight = halfHeight * Mathf.Cos(pitch) + radius * Mathf.Sin(pitch);
            Vector2 window = StereoVolume.IsReady ? StereoVolume.Window : new Vector2(0.598f, 0.336f);
            float popOutLimit = StereoVolume.IsReady ? StereoVolume.PopOutLimit : 0.13f;
            float depthLimit = StereoVolume.IsReady ? StereoVolume.DepthLimit : 0.30f;

            // Nearest point inside the pop-out limit.
            float byPopOut = popOutLimit - nearestMargin - depthReach;
            // Seen from the eye, what comes forward is magnified. The sides of the turning model can swing to its front, so its width is
            // magnified as at its nearest point, eye / (eye - p - depthReach); its top and bottom stay about its centre's depth, so its
            // height is magnified as at the centre, eye / (eye - p). Both stay inside the window.
            float byWidth = eye - depthReach - eye * radius / Mathf.Max(0.001f, window.x * 0.5f * windowWidthFill);
            float byHeight = eye - eye * screenHalfHeight / Mathf.Max(0.001f, window.y * 0.5f * windowHeightFill);
            // The far side inside the depth limit sets the least it may come out.
            float least = depthReach - (depthLimit - farMargin);

            float pop = maxPopOut;
            DepthLimiter = "max";
            if (byPopOut < pop) {
                pop = byPopOut;
                DepthLimiter = "pop-out";
            }
            if (byWidth < pop) {
                pop = byWidth;
                DepthLimiter = "width";
            }
            if (byHeight < pop) {
                pop = byHeight;
                DepthLimiter = "height";
            }
            if (pop < least) {
                pop = least;
                DepthLimiter = "depth";
            }

            AutoPopOut = pop;
            float scale = 1f;
            float tunedPop = 0f;
            IsTuned = tuning != null && tuning.TryGetPopOut(director.GetCentreTitle(), out tunedPop);
            if (IsTuned) {
                pop = Mathf.Clamp(tunedPop, 0f, maxPopOut);
                scale = LargestScaleAt(pop, eye, radius, depthReach, screenHalfHeight, window, popOutLimit, depthLimit);
            }

            _scaleFactor = scale;
            PopOut = pop;
            NearestPoint = pop + depthReach * scale;
            _homeDistance = eye - pop;
            viewer.FlyTo(homeYaw, homePitch, _homeDistance, animated, placeSeconds);
#if UNITY_EDITOR
            Debug.Log($"[Presentation View] {director.GetCentreTitle()}: {(IsTuned ? "tuned" : "auto")}, centre {pop:F3} m out, size x{scale:F2}, nearest {NearestPoint:F3} m out (limit {popOutLimit:F3}), decided by {DepthLimiter}.", this);
#endif
        }

        /// <summary>
        /// Largest size, over the fitted one, at which a model whose centre is <paramref name="pop"/> metres out still keeps its nearest
        /// point inside the pop-out limit, its far side inside the depth limit, and its magnified width and height inside the window.
        /// Sets <see cref="DepthLimiter"/> to the limit that decides it.
        /// </summary>
        private float LargestScaleAt(float pop, float eye, float radius, float depthReach, float screenHalfHeight, Vector2 window,
            float popOutLimit, float depthLimit) {
            float reach = Mathf.Max(0.0001f, depthReach);
            float halfWidthAllowed = window.x * 0.5f * windowWidthFill;
            float halfHeightAllowed = window.y * 0.5f * windowHeightFill;

            float scale = maxScale;
            DepthLimiter = "max";
            float byPopOut = (popOutLimit - nearestMargin - pop) / reach;
            if (byPopOut < scale) {
                scale = byPopOut;
                DepthLimiter = "pop-out";
            }
            float byDepth = (depthLimit - farMargin + pop) / reach;
            if (byDepth < scale) {
                scale = byDepth;
                DepthLimiter = "depth";
            }
            // Width at the nearest point: k r e / (e - p - k d) <= W  gives  k <= W (e - p) / (r e + W d).
            float byWidth = halfWidthAllowed * (eye - pop) / Mathf.Max(0.0001f, radius * eye + halfWidthAllowed * reach);
            if (byWidth < scale) {
                scale = byWidth;
                DepthLimiter = "width";
            }
            // Height at the centre: k h e / (e - p) <= H.
            float byHeight = halfHeightAllowed * (eye - pop) / Mathf.Max(0.0001f, screenHalfHeight * eye);
            if (byHeight < scale) {
                scale = byHeight;
                DepthLimiter = "height";
            }
            if (scale < minScale) {
                scale = minScale;
                DepthLimiter = "min";
            }

            return scale;
        }

        private static float EyeDistance() {
            return StereoVolume.IsReady ? StereoVolume.EyeDistance : 0.5f;
        }

        private void LimitToBudget() {
            if (depthKeeper == null || _zoomTarget <= 1f) {
                return;
            }

            float extent = depthKeeper.Extent;
            float budget = depthKeeper.Budget;
            if (extent <= budget) {
                return;
            }

            float fitting = Mathf.Max(1f, _zoom * budget * BudgetFit / extent);
            if (_zoomTarget > fitting + Settled) {
                _zoomTarget = fitting;
            }
        }

        private void OnDollyInput(float metres) {
            _zoomTarget = Mathf.Clamp(_zoomTarget * Mathf.Exp(metres * dollySensitivity), 1f, Mathf.Max(1f, maxZoom));
        }
    }
}
