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
        [SerializeField, Tooltip("Metres kept between the model's nearest point and the display's pop-out limit.")]
        private float nearestMargin = 0.02f;
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

        private Transform _tracked;
        private float _zoom = 1f;
        private float _zoomTarget = 1f;
        private float _zoomVelocity;
        private float _homeDistance;

        /// <summary>Metres the current model's centre stands in front of the glass at its home view.</summary>
        public float PopOut { get; private set; }

        /// <summary>Metres the current model's nearest point comes in front of the glass at its home view.</summary>
        public float NearestPoint { get; private set; }

        /// <summary>Which limit decided the current model's depth: "pop-out", "width", "height", "max" or "depth".</summary>
        public string DepthLimiter { get; private set; }

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
            viewer.FlyTo(homeYaw, homePitch, _homeDistance, true);
        }

        private void Update() {
            TrackCentreModel();
            LimitToBudget();
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomVelocity, zoomSmoothTime);
            content.localScale = Vector3.one * _zoom;
        }

        private void TrackCentreModel() {
            Transform centre = director.GetCentreHolder();
            if (centre == _tracked || (centre != null && !centre.gameObject.activeInHierarchy)) {
                return;
            }

            _tracked = centre;
            _zoom = 1f;
            _zoomTarget = 1f;
            _zoomVelocity = 0f;
            content.localScale = Vector3.one;
            if (depthKeeper != null) {
                depthKeeper.Track(centre);
            }

            if (centre != null) {
                PlaceCentreModel();
            }
        }

        /// <summary>Chooses the home distance for the model on stage and flies the rig there; see the class summary for the rules.</summary>
        private void PlaceCentreModel() {
            float radius;
            float halfHeight;
            if (!director.TryGetCentreExtents(out radius, out halfHeight)) {
                Debug.LogWarning($"[Presentation View] '{director.GetCentreTitle()}' has no measurable mesh; it stays on the glass.", this);
                PopOut = 0f;
                NearestPoint = 0f;
                DepthLimiter = "none";
                _homeDistance = EyeDistance();
                viewer.FlyTo(homeYaw, homePitch, _homeDistance, true, placeSeconds);
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

            PopOut = pop;
            NearestPoint = pop + depthReach;
            _homeDistance = eye - pop;
            viewer.FlyTo(homeYaw, homePitch, _homeDistance, true, placeSeconds);
#if UNITY_EDITOR
            Debug.Log($"[Presentation View] {director.GetCentreTitle()}: turn radius {radius:F3} m, half height {halfHeight:F3} m, centre {pop:F3} m out, nearest {NearestPoint:F3} m out (limit {popOutLimit:F3}), decided by {DepthLimiter}.", this);
#endif
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
