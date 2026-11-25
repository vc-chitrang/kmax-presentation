#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>
    /// Editor-only audit. Shows every model in turn, follows its whole show (turn, sway, explode, engine, aircraft) and projects the
    /// outermost vertices of its meshes into the fit camera's view, then scales the item's <c>scaleMultiplier</c> so the model's largest
    /// excursion touches the allowed screen band (padding on every side) and no more. Repeats until every model is within tolerance and
    /// writes the per-model numbers to a JSON report that an editor script writes back into the scene.
    /// </summary>
    public sealed class PresentationFitAudit : MonoBehaviour {
        private const int DirectionCount = 26;
        private static readonly Vector3[] Directions = BuildDirections();

        public bool Finished { get; private set; }
        public string Report { get; private set; }

        private PresentationDirector director;
        private Camera viewCamera;
        private float padding;
        private float sideMargin;
        private float sampleSeconds;
        private int maxPasses;
        private string reportPath;

        public void Begin(PresentationDirector owner, Camera camera, float topBottomPadding, float sidePadding, float seconds, int passes, string path) {
            director = owner;
            viewCamera = camera;
            padding = topBottomPadding;
            sideMargin = sidePadding;
            sampleSeconds = seconds;
            maxPasses = passes;
            reportPath = path;
            StartCoroutine(Run());
        }

        private IEnumerator Run() {
            director.AutoAdvance = false;
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("index,title,multiplier,offsetX,offsetY,offsetZ,passes,yMin,yMax,xMin,xMax,status");
            int count = director.ItemCount;
            for (int i = 0; i < count; i++) {
                PresentationItem item = director.GetItem(i);
                string status = "ok";
                float yMin = 0.0f;
                float yMax = 0.0f;
                float xMin = 0.0f;
                float xMax = 0.0f;
                int pass = 0;
                for (pass = 1; pass <= maxPasses; pass++) {
                    director.JumpToItem(i);
                    yield return null;
                    yield return StartCoroutine(Measure(i, delegate(float a, float b, float c, float d) {
                        yMin = a;
                        yMax = b;
                        xMin = c;
                        xMax = d;
                    }));
                    // Centre the model's whole excursion on the screen first, then fit it to the band.
                    float centreX = (xMin + xMax) * 0.5f;
                    float centreY = (yMin + yMax) * 0.5f;
                    bool centred = Mathf.Abs(centreX - 0.5f) <= 0.008f && Mathf.Abs(centreY - 0.5f) <= 0.008f;
                    if (!centred) {
                        Transform holder = director.GetCentreHolder();
                        Matrix4x4 projection = viewCamera.projectionMatrix;
                        float depth = Mathf.Max(0.1f, Vector3.Dot(holder.position - viewCamera.transform.position, viewCamera.transform.forward));
                        float visibleHeight = 2.0f * depth / Mathf.Max(0.0001f, Mathf.Abs(projection.m11));
                        float visibleWidth = 2.0f * depth / Mathf.Max(0.0001f, Mathf.Abs(projection.m00));
                        item.positionOffset = item.positionOffset
                            + viewCamera.transform.right * ((0.5f - centreX) * visibleWidth)
                            + viewCamera.transform.up * ((0.5f - centreY) * visibleHeight);
                    }
                    float factor = Factor(yMin, yMax, xMin, xMax);
                    if (centred && Mathf.Abs(factor - 1.0f) <= 0.015f) {
                        break;
                    }
                    float next = Mathf.Clamp(item.scaleMultiplier * factor, 0.05f, 4.0f);
                    item.scaleMultiplier = next;
                    if (pass == maxPasses) {
                        status = "not-converged";
                    }
                }
                csv.AppendLine(i + "," + item.title.Replace(",", ";") + "," + item.scaleMultiplier.ToString("F4") + ","
                    + item.positionOffset.x.ToString("F4") + "," + item.positionOffset.y.ToString("F4") + "," + item.positionOffset.z.ToString("F4") + "," + pass + ","
                    + yMin.ToString("F3") + "," + yMax.ToString("F3") + "," + xMin.ToString("F3") + "," + xMax.ToString("F3") + "," + status);
            }
            Report = csv.ToString();
            System.IO.File.WriteAllText(reportPath, Report);
            Finished = true;
            director.AutoAdvance = true;
        }

        // Scale that brings the largest excursion to the edge of the allowed band; above 1 grows a small model, below 1 shrinks a large one.
        private float Factor(float yMin, float yMax, float xMin, float xMax) {
            float centreY = (yMin + yMax) * 0.5f;
            float centreX = (xMin + xMax) * 0.5f;
            float halfY = Mathf.Max(0.0001f, (yMax - yMin) * 0.5f);
            float halfX = Mathf.Max(0.0001f, (xMax - xMin) * 0.5f);
            float allowedUp = 1.0f - padding - centreY;
            float allowedDown = centreY - padding;
            float allowedRight = 1.0f - sideMargin - centreX;
            float allowedLeft = centreX - sideMargin;
            float byY = Mathf.Min(allowedUp, allowedDown) / halfY;
            float byX = Mathf.Min(allowedRight, allowedLeft) / halfX;
            return Mathf.Min(byY, byX);
        }

        private IEnumerator Measure(int index, System.Action<float, float, float, float> done) {
            Transform holder = director.GetCentreHolder();
            Transform model = director.GetCentreModel();
            List<Renderer> renderers = new List<Renderer>();
            List<Vector3[]> extremes = new List<Vector3[]>();
            Renderer[] all = model.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < all.Length; r++) {
                if (all[r] is MeshRenderer || all[r] is SkinnedMeshRenderer) {
                    if (all[r].name.Contains("Halo") || all[r].name.Contains("Billboard")) {
                        continue;
                    }
                    renderers.Add(all[r]);
                    extremes.Add(FindExtremes(all[r]));
                }
            }

            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            float xMin = float.MaxValue;
            float xMax = float.MinValue;
            float start = Time.time;
            while (Time.time - start < sampleSeconds) {
                yield return null;
                if (holder == null || holder.localScale.x < 0.995f) {
                    continue;
                }
                for (int r = 0; r < renderers.Count; r++) {
                    Renderer renderer = renderers[r];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) {
                        continue;
                    }
                    Vector3[] points = extremes[r];
                    for (int p = 0; p < points.Length; p++) {
                        Vector3 view = viewCamera.WorldToViewportPoint(renderer.transform.TransformPoint(points[p]));
                        if (view.z <= 0.0f) {
                            continue;
                        }
                        yMin = Mathf.Min(yMin, view.y);
                        yMax = Mathf.Max(yMax, view.y);
                        xMin = Mathf.Min(xMin, view.x);
                        xMax = Mathf.Max(xMax, view.x);
                    }
                }
            }
            done(yMin, yMax, xMin, xMax);
        }

        private static Vector3[] FindExtremes(Renderer renderer) {
            Mesh mesh = null;
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null) {
                mesh = skinned.sharedMesh;
            } else {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) {
                return BoxCorners(renderer.localBounds);
            }
            Vector3[] vertices = mesh.vertices;
            Vector3[] found = new Vector3[DirectionCount];
            float[] best = new float[DirectionCount];
            for (int d = 0; d < DirectionCount; d++) {
                best[d] = float.MinValue;
            }
            for (int v = 0; v < vertices.Length; v++) {
                for (int d = 0; d < DirectionCount; d++) {
                    float along = Vector3.Dot(vertices[v], Directions[d]);
                    if (along > best[d]) {
                        best[d] = along;
                        found[d] = vertices[v];
                    }
                }
            }
            return found;
        }

        private static Vector3[] BoxCorners(Bounds local) {
            Vector3[] corners = new Vector3[8];
            for (int corner = 0; corner < 8; corner++) {
                corners[corner] = local.center + new Vector3(
                    (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                    (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                    (corner & 4) == 0 ? -local.extents.z : local.extents.z);
            }
            return corners;
        }

        private static Vector3[] BuildDirections() {
            Vector3[] directions = new Vector3[DirectionCount];
            int count = 0;
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    for (int z = -1; z <= 1; z++) {
                        if (x != 0 || y != 0 || z != 0) {
                            directions[count] = new Vector3(x, y, z).normalized;
                            count++;
                        }
                    }
                }
            }
            return directions;
        }
    }
}
#endif
