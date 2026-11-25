using UnityEngine;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>
    /// Turns the backdrop sky very slowly around the vertical axis, 0 to 360 degrees and round again without a seam.
    /// Put it on the BackdropSphere: the BackdropSphere shader samples the sky by object-space direction, so turning the
    /// transform turns the sky and no material asset is touched. The angle is computed from time (not accumulated), so it never drifts.
    /// </summary>
    public sealed class BackdropRotator : MonoBehaviour {
        [Tooltip("Degrees per second. 0.5 = one full turn every 12 minutes.")]
        [SerializeField] [Range(0.0f, 10.0f)] private float degreesPerSecond = 0.5f;
        [Tooltip("Angle the sky starts at, in degrees.")]
        [SerializeField] [Range(0.0f, 360.0f)] private float startAngle = 0.0f;

        private Quaternion baseRotation;
        private float elapsed;

        private void Awake() {
            baseRotation = transform.localRotation;
        }

        private void Update() {
            elapsed += Time.unscaledDeltaTime;
            float angle = Mathf.Repeat(startAngle + elapsed * degreesPerSecond, 360.0f);
            transform.localRotation = Quaternion.Euler(0.0f, angle, 0.0f) * baseRotation;
        }
    }
}
