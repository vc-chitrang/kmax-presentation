using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;

namespace ViitorCloud.KmaxPresentation.Editor {
    /// <summary>
    /// Inspector for the Presentation Director, the Editor's tuning panel for the showcase.
    ///
    /// <para>Edit mode: a Tuning mode switch (the director's browse mode: the show holds each model and Previous / Next appear at the
    /// bottom centre of the screen; never in a player build).</para>
    ///
    /// <para>Play mode: Previous / Next, and for the model on stage a pop-out slider in centimetres. The model's size follows the slider
    /// (the largest that still fits the comfort and window limits at that depth), with readouts of the size, the nearest point and the
    /// limit in charge. Auto returns the model to automatic placement. Save writes every model's choice to the tuning asset, which the
    /// next Play and every build read; rebuilding the scene does not touch it.</para>
    /// </summary>
    [CustomEditor(typeof(PresentationDirector))]
    public sealed class PresentationDirectorEditor : UnityEditor.Editor {
        private const float CentimetresPerMetre = 100f;

        public override void OnInspectorGUI() {
            PresentationDirector director = (PresentationDirector)target;
            PresentationViewControl view = director.GetComponent<PresentationViewControl>();

            if (Application.isPlaying) {
                DrawPlayPanel(director, view);
            } else {
                DrawEditPanel();
            }

            EditorGUILayout.Space();
            DrawScalePanel(director);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        /// <summary>
        /// Manual scale: in Play, a slider for the model on stage (the same value as its Scale in the Items list); in both modes, Save
        /// scales writes every item's Scale into the tuning asset on disk, so it survives stopping Play and rebuilding the scene.
        /// </summary>
        private static void DrawScalePanel(PresentationDirector director) {
            EditorGUILayout.LabelField("Manual scale", EditorStyles.boldLabel);
            if (Application.isPlaying) {
                PresentationItem item = director.GetCentreItem();
                if (item != null) {
                    float chosen = EditorGUILayout.Slider(new GUIContent("Scale: " + item.title, "Size over the automatic fit. The camera then moves so the model stays inside the comfort limits."),
                        item.scale, PresentationTuning.MinManualScale, PresentationTuning.MaxManualScale);
                    if (!Mathf.Approximately(chosen, item.scale)) {
                        item.scale = chosen;
                    }
                }
            }

            if (director.Tuning == null) {
                EditorGUILayout.HelpBox("No tuning asset is assigned to the director; rebuild the showcase scene.", MessageType.Warning);
                return;
            }

            if (!Application.isPlaying) {
                // Saved scales are loaded into the items only when the scene starts, so saving from Edit mode could overwrite them.
                EditorGUILayout.HelpBox("Tune and save scales in Play mode: the saved values are loaded into the Items list when Play starts.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Save scales (all models)", GUILayout.Height(24f))) {
                int stored = director.StoreScalesInTuning();
                EditorUtility.SetDirty(director.Tuning);
                AssetDatabase.SaveAssetIfDirty(director.Tuning);
                Debug.Log("[Presentation Tuning] Saved scales: " + stored + " models differ from 1, in " + AssetDatabase.GetAssetPath(director.Tuning) + ".");
            }
        }

        private void DrawEditPanel() {
            SerializedProperty browse = serializedObject.FindProperty("debugBrowse");
            if (browse == null) {
                return;
            }

            serializedObject.Update();
            EditorGUILayout.LabelField("Tuning", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(browse.boolValue
                ? "Tuning mode is ON: in Play the show holds each model, with Previous / Next at the bottom centre and the pop-out slider here. Switch it off before saving the scene for a build (a player ignores it anyway)."
                : "Tuning mode is OFF: Play runs the automatic show. Switch it on to check and tune each model by hand.", MessageType.Info);
            if (GUILayout.Button(browse.boolValue ? "Switch tuning mode off" : "Switch tuning mode on", GUILayout.Height(26f))) {
                browse.boolValue = !browse.boolValue;
                serializedObject.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(((PresentationDirector)target).gameObject.scene);
            }
        }

        private static void DrawPlayPanel(PresentationDirector director, PresentationViewControl view) {
            EditorGUILayout.LabelField("Tuning", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Model", (director.BrowseIndex + 1) + " / " + director.ItemCount + "   " + director.GetCentreTitle());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous")) {
                director.BrowseOffset(-1);
            }
            if (GUILayout.Button("Next")) {
                director.BrowseOffset(1);
            }
            EditorGUILayout.EndHorizontal();

            if (view == null) {
                EditorGUILayout.HelpBox("No PresentationViewControl on this object; rebuild the showcase scene.", MessageType.Warning);
                return;
            }
            if (view.Tuning == null) {
                EditorGUILayout.HelpBox("No tuning asset is assigned to the view control; rebuild the showcase scene.", MessageType.Warning);
                return;
            }

            float maxCm = view.MaxPopOut * CentimetresPerMetre;
            float currentCm = view.PopOut * CentimetresPerMetre;
            EditorGUI.BeginChangeCheck();
            float chosenCm = EditorGUILayout.Slider(new GUIContent("Pop-out (cm)", "How far the model's centre stands in front of the glass. More: smaller and floating. Less: bigger, on the glass."), currentCm, 0f, maxCm);
            if (EditorGUI.EndChangeCheck()) {
                view.ApplyPopOut(chosenCm / CentimetresPerMetre);
                EditorUtility.SetDirty(view.Tuning);
            }

            EditorGUILayout.LabelField("Mode", view.IsTuned ? "Tuned" : "Automatic (" + (view.AutoPopOut * CentimetresPerMetre).ToString("F1") + " cm)");
            EditorGUILayout.LabelField("Size", "x" + view.ScaleFactor.ToString("F2") + " of the automatic fit");
            EditorGUILayout.LabelField("Nearest point", (view.NearestPoint * CentimetresPerMetre).ToString("F1") + " cm in front of the glass (limit 13 cm)");
            EditorGUILayout.LabelField("Decided by", view.DepthLimiter);
            if (view.IsTuned && (view.DepthLimiter == "min" || view.DepthLimiter == "max")) {
                EditorGUILayout.HelpBox(view.DepthLimiter == "min"
                    ? "At this pop-out the model would have to shrink below the smallest size allowed; lower the pop-out."
                    : "The model is at the largest size allowed; more pop-out will not make it smaller until a limit is reached.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto (this model)")) {
                view.UseAutomatic();
                EditorUtility.SetDirty(view.Tuning);
            }
            if (GUILayout.Button("Save all", GUILayout.Height(24f))) {
                EditorUtility.SetDirty(view.Tuning);
                AssetDatabase.SaveAssetIfDirty(view.Tuning);
                Debug.Log("[Presentation Tuning] Saved " + view.Tuning.Count + " tuned models to " + AssetDatabase.GetAssetPath(view.Tuning) + ".");
            }
            EditorGUILayout.EndHorizontal();
        }

        public override bool RequiresConstantRepaint() {
            return Application.isPlaying;
        }
    }
}
