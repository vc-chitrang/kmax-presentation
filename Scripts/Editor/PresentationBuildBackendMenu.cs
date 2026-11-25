using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Chooses the scripting backend and cache behaviour of the Windows builds made from the VC Build Panel:
    /// Mono with the build cache kept (a test build in a fraction of the time) or IL2CPP with a clean cache (the final build).
    /// The choice is the editor preference read by BuildBaseWindows / BuildBase.
    /// </summary>
    public static class PresentationBuildBackendMenu {
        private const string FastMonoBuildKey = "KmaxPresentation.FastMonoBuild";
        private const string FastMenu = "KMax/Presentation/Build Backend/Mono - fast test build";
        private const string FinalMenu = "KMax/Presentation/Build Backend/IL2CPP - final build";

        [MenuItem(FastMenu)]
        public static void UseMono() {
            Apply(true);
        }

        [MenuItem(FastMenu, true)]
        private static bool ValidateMono() {
            Menu.SetChecked(FastMenu, EditorPrefs.GetBool(FastMonoBuildKey, false));
            return true;
        }

        [MenuItem(FinalMenu)]
        public static void UseIl2Cpp() {
            Apply(false);
        }

        [MenuItem(FinalMenu, true)]
        private static bool ValidateIl2Cpp() {
            Menu.SetChecked(FinalMenu, !EditorPrefs.GetBool(FastMonoBuildKey, false));
            return true;
        }

        public static void Apply(bool fastMono) {
            EditorPrefs.SetBool(FastMonoBuildKey, fastMono);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, fastMono ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
            Debug.Log("[Presentation] Windows builds now use " + (fastMono ? "Mono with the build cache kept (fast test build)." : "IL2CPP with a clean cache (final build)."));
        }
    }
}
