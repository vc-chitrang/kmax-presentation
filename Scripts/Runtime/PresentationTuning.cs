using System;
using System.Collections.Generic;

using UnityEngine;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>One model's saved tuning, found by the model's title.</summary>
    [Serializable]
    public class PresentationTuningEntry {
        public string title;
        [Tooltip("True when a pop-out is saved for this model; false when only its manual scale is.")]
        public bool hasPopOut;
        [Tooltip("Metres the model's centre stands in front of the glass. Its size follows: the largest that still fits the comfort limits at this depth.")]
        public float popOut;
        [Tooltip("Size over the automatic fit that the last pop-out save produced. Read only: written for reference, never applied.")]
        public float savedScale = 1f;
        [Tooltip("Manual size over the automatic fit, set with the item's Scale slider on the director and kept by Save scales.")]
        public float scale = 1f;
    }

    /// <summary>
    /// Per-model tuning for the presentation showcase, set in the Editor and kept in its own asset, so rebuilding the scene never
    /// overwrites it: a manual scale (the director's item Scale slider, loaded into the items when the scene starts) and a pop-out
    /// (the view control's slider). A model with no entry is automatic.
    /// </summary>
    [CreateAssetMenu(fileName = "PresentationTuning", menuName = "KMax/Presentation/Tuning")]
    public class PresentationTuning : ScriptableObject {
        public const float MinManualScale = 0.3f;
        /// <summary>Raised from 2.0 to 3.0 (2026-11-26) so the engines can reach the size they have in their own scenes (about 2.2 and 2.9 times the fit).</summary>
        public const float MaxManualScale = 3.0f;

        [SerializeField] private List<PresentationTuningEntry> entries = new List<PresentationTuningEntry>();

        public int Count {
            get {
                return entries.Count;
            }
        }

        /// <summary>The saved pop-out of the model titled <paramref name="title"/>; false when the model's depth is automatic.</summary>
        public bool TryGetPopOut(string title, out float popOut) {
            PresentationTuningEntry entry = Find(title);
            bool found = entry != null && entry.hasPopOut;
            popOut = found ? entry.popOut : 0f;
            return found;
        }

        /// <summary>Sets the pop-out of a model, creating its entry when it has none.</summary>
        public void SetPopOut(string title, float popOut, float scale) {
            PresentationTuningEntry entry = FindOrCreate(title);
            if (entry == null) {
                return;
            }

            entry.hasPopOut = true;
            entry.popOut = popOut;
            entry.savedScale = scale;
        }

        /// <summary>Returns a model's depth to automatic placement; its manual scale is kept.</summary>
        public void ClearPopOut(string title) {
            PresentationTuningEntry entry = Find(title);
            if (entry == null) {
                return;
            }

            entry.hasPopOut = false;
            entry.popOut = 0f;
            entry.savedScale = 1f;
            RemoveIfEmpty(entry);
        }

        /// <summary>The saved manual scale of the model titled <paramref name="title"/>; false when none is saved.</summary>
        public bool TryGetScale(string title, out float scale) {
            PresentationTuningEntry entry = Find(title);
            bool found = entry != null && !Mathf.Approximately(entry.scale, 1f);
            scale = found ? Mathf.Clamp(entry.scale, MinManualScale, MaxManualScale) : 1f;
            return found;
        }

        /// <summary>Sets the manual scale of a model; a scale of 1 removes it again.</summary>
        public void SetScale(string title, float scale) {
            float clamped = Mathf.Clamp(scale, MinManualScale, MaxManualScale);
            if (Mathf.Approximately(clamped, 1f)) {
                PresentationTuningEntry existing = Find(title);
                if (existing != null) {
                    existing.scale = 1f;
                    RemoveIfEmpty(existing);
                }
                return;
            }

            PresentationTuningEntry entry = FindOrCreate(title);
            if (entry != null) {
                entry.scale = clamped;
            }
        }

        private PresentationTuningEntry FindOrCreate(string title) {
            if (string.IsNullOrEmpty(title)) {
                Debug.LogWarning("[Presentation Tuning] A model without a title cannot be tuned.");
                return null;
            }

            PresentationTuningEntry entry = Find(title);
            if (entry == null) {
                entry = new PresentationTuningEntry();
                entry.title = title;
                entries.Add(entry);
            }

            return entry;
        }

        private void RemoveIfEmpty(PresentationTuningEntry entry) {
            if (!entry.hasPopOut && Mathf.Approximately(entry.scale, 1f)) {
                entries.Remove(entry);
            }
        }

        private PresentationTuningEntry Find(string title) {
            for (int i = 0; i < entries.Count; i++) {
                if (entries[i].title == title) {
                    return entries[i];
                }
            }

            return null;
        }
    }
}
