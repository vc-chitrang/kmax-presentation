using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Inspector for the Presentation Director: Previous / Next / Save tuning buttons for browsing the models in Play.
    /// Values changed in Play are lost when Play stops, so "Save tuning" keeps every item's position offset, scale multiplier and
    /// top/bottom padding, and the moment the editor is back in Edit mode they are written into the scene's director (undoable) and the
    /// scene is marked dirty. Save the scene (Ctrl+S) to keep them on disk.
    /// </summary>
    [CustomEditor(typeof(PresentationDirector))]
    public sealed class PresentationDirectorEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            PresentationDirector director = (PresentationDirector)target;

            if (Application.isPlaying) {
                EditorGUILayout.HelpBox("Browse: " + director.BrowseIndex + " / " + director.ItemCount + ". Edit Items > element " + director.BrowseIndex + " > Placement tuning, then press Save tuning.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous")) {
                    director.BrowseOffset(-1);
                }
                if (GUILayout.Button("Next")) {
                    director.BrowseOffset(1);
                }
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Save tuning (keep after Play)", GUILayout.Height(28.0f))) {
                    PresentationTuningStore.Save(director);
                }
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();
        }
    }

    /// <summary>Carries the placement tuning out of Play mode and writes it back into the scene director.</summary>
    [InitializeOnLoad]
    public static class PresentationTuningStore {
        private const string SessionKey = "ViitorCloud.KmaxPresentation.PendingTuning";

        [Serializable]
        private sealed class TuningEntry {
            public int index;
            public string title;
            public Vector3 positionOffset;
            public float scaleMultiplier;
            public float topBottomPadding;
        }

        [Serializable]
        private sealed class TuningFile {
            public List<TuningEntry> entries = new List<TuningEntry>();
        }

        static PresentationTuningStore() {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += ApplyPendingIfEditMode;
        }

        public static void Save(PresentationDirector director) {
            TuningFile file = new TuningFile();
            for (int i = 0; i < director.ItemCount; i++) {
                PresentationItem item = director.GetItem(i);
                TuningEntry entry = new TuningEntry();
                entry.index = i;
                entry.title = item.title;
                entry.positionOffset = item.positionOffset;
                entry.scaleMultiplier = item.scaleMultiplier;
                entry.topBottomPadding = item.topBottomPadding;
                file.entries.Add(entry);
            }
            SessionState.SetString(SessionKey, JsonUtility.ToJson(file));
            Debug.Log("[Presentation Tuning] Saved " + file.entries.Count + " items. They are written into the scene when Play stops.");
        }

        private static void OnPlayModeChanged(PlayModeStateChange change) {
            if (change == PlayModeStateChange.EnteredEditMode) {
                ApplyPendingIfEditMode();
            }
        }

        private static void ApplyPendingIfEditMode() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }
            string json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) {
                return;
            }
            SessionState.EraseString(SessionKey);

            TuningFile file = JsonUtility.FromJson<TuningFile>(json);
            PresentationDirector[] directors = UnityEngine.Object.FindObjectsByType<PresentationDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (file == null || directors.Length == 0) {
                Debug.LogError("[Presentation Tuning] Saved tuning could not be applied: no Presentation Director in the open scene.");
                return;
            }

            for (int d = 0; d < directors.Length; d++) {
                SerializedObject serialized = new SerializedObject(directors[d]);
                SerializedProperty itemsProperty = serialized.FindProperty("items");
                int applied = 0;
                for (int e = 0; e < file.entries.Count; e++) {
                    TuningEntry entry = file.entries[e];
                    if (entry.index < 0 || entry.index >= itemsProperty.arraySize) {
                        continue;
                    }
                    SerializedProperty element = itemsProperty.GetArrayElementAtIndex(entry.index);
                    if (element.FindPropertyRelative("title").stringValue != entry.title) {
                        Debug.LogWarning("[Presentation Tuning] Item " + entry.index + " is now '" + element.FindPropertyRelative("title").stringValue + "', not '" + entry.title + "'; skipped.");
                        continue;
                    }
                    element.FindPropertyRelative("positionOffset").vector3Value = entry.positionOffset;
                    element.FindPropertyRelative("scaleMultiplier").floatValue = entry.scaleMultiplier;
                    element.FindPropertyRelative("topBottomPadding").floatValue = entry.topBottomPadding;
                    applied++;
                }
                serialized.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(directors[d].gameObject.scene);
                Debug.Log("[Presentation Tuning] Applied tuning of " + applied + " items to '" + directors[d].gameObject.scene.name + "'. Save the scene (Ctrl+S) to keep it.");
            }
        }
    }
}
