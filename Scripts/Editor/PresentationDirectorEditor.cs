using UnityEditor;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Inspector for the Presentation Director: Previous / Next buttons for browsing the models in Play. Every model is fitted by the
    /// director alone; there is no per-model placement or scale tuning.
    /// </summary>
    [CustomEditor(typeof(PresentationDirector))]
    public sealed class PresentationDirectorEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            PresentationDirector director = (PresentationDirector)target;

            if (Application.isPlaying) {
                EditorGUILayout.HelpBox("Browse: " + director.BrowseIndex + " / " + director.ItemCount + ".", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous")) {
                    director.BrowseOffset(-1);
                }
                if (GUILayout.Button("Next")) {
                    director.BrowseOffset(1);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();
        }
    }
}
