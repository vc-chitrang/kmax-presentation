using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// URP keeps only the shader variants and renderer resources of the pipeline assets set in Graphics / Quality settings when a
    /// player is built. The presentation scene switches to PresentationURP at runtime (soft 4K shadows, SSAO, MSAA), which is in
    /// neither, so its variants and the SSAO resources were stripped and the player rendered dark with broken passes.
    /// While the presentation profile is being built this makes PresentationURP the default and current-quality pipeline (before
    /// URP gathers its assets), and puts the previous assets back afterwards. A failed build is restored on the next editor load.
    /// </summary>
    [InitializeOnLoad]
    public sealed class PresentationBuildPipeline : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
        private const string ActiveGameInfoKey = "VC_ACTIVE_GAMEINFO_SO";
        private const string PresentationProfilePath = "Assets/Games/kmax-presentation/Game Info So.asset";
        private const string PresentationPipelinePath = "Assets/Games/kmax-presentation/Rendering/PresentationURP.asset";
        private const string SavedDefaultKey = "KmaxPresentation.SavedDefaultPipeline";
        private const string SavedQualityKey = "KmaxPresentation.SavedQualityPipeline";
        private const string SwappedKey = "KmaxPresentation.PipelineSwapped";

        public int callbackOrder {
            get {
                return int.MinValue;
            }
        }

        static PresentationBuildPipeline() {
            EditorApplication.delayCall += RestoreIfLeftSwapped;
        }

        public void OnPreprocessBuild(BuildReport report) {
            if (EditorPrefs.GetString(ActiveGameInfoKey, string.Empty) != PresentationProfilePath) {
                return;
            }
            RenderPipelineAsset presentation = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PresentationPipelinePath);
            if (presentation == null) {
                Debug.LogError("[Presentation] " + PresentationPipelinePath + " is missing; the player would lose its shadows and SSAO.");
                return;
            }
            if (!EditorPrefs.GetBool(SwappedKey, false)) {
                EditorPrefs.SetString(SavedDefaultKey, AssetPath(GraphicsSettings.defaultRenderPipeline));
                EditorPrefs.SetString(SavedQualityKey, AssetPath(QualitySettings.renderPipeline));
                EditorPrefs.SetBool(SwappedKey, true);
            }
            GraphicsSettings.defaultRenderPipeline = presentation;
            QualitySettings.renderPipeline = presentation;
            Debug.Log("[Presentation] Building with " + presentation.name + " as the pipeline so its shader variants and SSAO are kept.");
        }

        public void OnPostprocessBuild(BuildReport report) {
            Restore();
        }

        private static void RestoreIfLeftSwapped() {
            if (EditorPrefs.GetBool(SwappedKey, false) && !BuildPipeline.isBuildingPlayer) {
                Restore();
            }
        }

        private static void Restore() {
            if (!EditorPrefs.GetBool(SwappedKey, false)) {
                return;
            }
            GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(EditorPrefs.GetString(SavedDefaultKey, string.Empty));
            QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(EditorPrefs.GetString(SavedQualityKey, string.Empty));
            EditorPrefs.SetBool(SwappedKey, false);
            Debug.Log("[Presentation] Pipeline settings restored after the presentation build.");
        }

        private static string AssetPath(Object asset) {
            return asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
        }
    }
}
