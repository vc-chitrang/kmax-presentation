using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>
    /// High-quality rendering for the presentation scene only. While the scene runs it switches the active render pipeline to the
    /// presentation URP asset (soft 4K shadows, MSAA, SSAO on its renderer) and gives every camera of the Kmax rig SMAA and
    /// post-processing; the previous pipeline asset is restored when the scene ends, so the launcher scenes keep their own settings.
    /// </summary>
    public sealed class PresentationQuality : MonoBehaviour {
        [SerializeField] private UniversalRenderPipelineAsset pipelineAsset;
        [SerializeField] private AntialiasingMode cameraAntialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        [SerializeField] private AntialiasingQuality cameraAntialiasingQuality = AntialiasingQuality.High;

        private RenderPipelineAsset previousQualityPipeline;
        private bool pipelineSwapped;

        private void Awake() {
            if (pipelineAsset == null) {
                Debug.LogError("[Presentation] PresentationQuality has no pipeline asset; the default pipeline stays active.");
                return;
            }
            previousQualityPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipelineAsset;
            pipelineSwapped = true;
        }

        private void Start() {
            EnsureAmbientLight();
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                UniversalAdditionalCameraData data = cameras[i].GetUniversalAdditionalCameraData();
                if (data == null) {
                    continue;
                }
                data.antialiasing = cameraAntialiasing;
                data.antialiasingQuality = cameraAntialiasingQuality;
                data.renderPostProcessing = true;
                data.renderShadows = true;
            }
        }

        // A player reads the ambient light from the scene's baked lighting data; the editor recomputes it live. When the scene has
        // no lighting data the player starts with black ambient (dark, flat models), so the trilight colours are turned into the
        // ambient probe here.
        private static void EnsureAmbientLight() {
            SphericalHarmonicsL2 current = RenderSettings.ambientProbe;
            float energy = Mathf.Abs(current[0, 0]) + Mathf.Abs(current[1, 0]) + Mathf.Abs(current[2, 0]);
            if (energy > 0.01f || RenderSettings.ambientMode != AmbientMode.Trilight) {
                return;
            }
            Color sky = RenderSettings.ambientSkyColor.linear;
            Color equator = RenderSettings.ambientEquatorColor.linear;
            Color ground = RenderSettings.ambientGroundColor.linear;
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(equator);
            probe.AddDirectionalLight(Vector3.up, (sky - equator) * 0.5f, 1.0f);
            probe.AddDirectionalLight(Vector3.down, (ground - equator) * 0.5f, 1.0f);
            RenderSettings.ambientProbe = probe * RenderSettings.ambientIntensity;
            Debug.Log("[Presentation] No baked ambient light found; built it from the trilight colours.");
        }

        private void OnDestroy() {
            if (pipelineSwapped) {
                QualitySettings.renderPipeline = previousQualityPipeline;
                pipelineSwapped = false;
            }
        }
    }
}
