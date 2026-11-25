using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Writes Assets/Games/kmax-presentation/Resources/PresentationBuildInfo.txt as "version|versionCode" from the active
    /// ViitorCloud Game Info profile (EditorPrefs VC_ACTIVE_GAMEINFO_SO, chosen in the VC Build Panel) before every build and
    /// whenever Play starts, so the on-screen version label always matches the build being made.
    /// </summary>
    [InitializeOnLoad]
    public sealed class PresentationBuildInfoWriter : IPreprocessBuildWithReport {
        private const string ActiveGameInfoKey = "VC_ACTIVE_GAMEINFO_SO";
        private const string OutputPath = "Assets/Games/kmax-presentation/Resources/PresentationBuildInfo.txt";

        public int callbackOrder {
            get {
                return 0;
            }
        }

        static PresentationBuildInfoWriter() {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public void OnPreprocessBuild(BuildReport report) {
            Write();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change) {
            if (change == PlayModeStateChange.ExitingEditMode) {
                Write();
            }
        }

        [MenuItem("KMax/Presentation/Write Build Version Info")]
        public static void Write() {
            string version = PlayerSettings.bundleVersion;
            string code = PlayerSettings.Android.bundleVersionCode.ToString();

            string profilePath = EditorPrefs.GetString(ActiveGameInfoKey, string.Empty);
            Object profile = string.IsNullOrEmpty(profilePath) ? null : AssetDatabase.LoadMainAssetAtPath(profilePath);
            if (profile != null) {
                SerializedObject serialized = new SerializedObject(profile);
                SerializedProperty versionProperty = serialized.FindProperty("value.properties.version");
                SerializedProperty codeProperty = serialized.FindProperty("value.properties.versionCode");
                if (versionProperty != null && !string.IsNullOrEmpty(versionProperty.stringValue)) {
                    version = versionProperty.stringValue;
                }
                if (codeProperty != null) {
                    code = codeProperty.intValue.ToString();
                }
            } else {
                Debug.LogWarning("[Presentation] No active Game Info profile; the version label uses Player Settings.");
            }

            string content = version + "|" + code;
            string fullPath = Path.GetFullPath(OutputPath);
            if (File.Exists(fullPath) && File.ReadAllText(fullPath) == content) {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Presentation] Build version info written: v" + version + " (" + code + ").");
        }
    }
}
