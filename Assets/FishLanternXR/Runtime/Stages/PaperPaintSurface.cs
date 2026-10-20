using System;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Owns one paper patch's real strokes and spatial coverage, using its independent drawing UVs.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity), typeof(InteractionGate))]
    public sealed class PaperPaintSurface : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private string surfaceLabel;
        [SerializeField] private MeshFilter drawingMesh;
        [SerializeField] private Renderer drawingRenderer;
        [SerializeField] private Renderer[] additionalRenderers = Array.Empty<Renderer>();
        [SerializeField] private PaperPaintTool[] tools = Array.Empty<PaperPaintTool>();
        [SerializeField] private Texture2D sketchMask;
        [SerializeField] private Texture2D paintMask;
        [SerializeField, Range(0.001f, 0.01f)] private float contactDistance = 0.006f;
        [SerializeField, Range(0.5f, 1f)] private float requiredCoverage = 0.85f;
        [SerializeField, Range(0.1f, 1f)] private float requiredTileCoverage = 0.6f;
        [SerializeField] private Color redPigment = new(0.65f, 0.08f, 0.04f, 1f);
        [SerializeField] private Color greenPigment = new(0.1f, 0.35f, 0.2f, 1f);
        [SerializeField] private Color blackPigment = new(0.06f, 0.045f, 0.035f, 1f);
        [SerializeField] private InteractionEventUnityEvent onProgress = new();
        [SerializeField] private InteractionEventUnityEvent onCompleted = new();

        private const int TextureSize = 512;
        private const int TileCount = 16;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color32 PencilColor = new(50, 43, 36, 230);

        private struct Triangle
        {
            public Vector3 A, B, C, TangentU, TangentV;
            public Vector2 UvA, UvB, UvC;
        }

        private struct StrokeSample
        {
            public bool Active;
            public Vector2 Uv;
            public Vector3 Position;
            public float Time;
            public string Color;
        }

        private InteractableIdentity identity;
        private InteractionGate gate;
        private Triangle[] triangles;
        private StrokeSample[] previousSamples;
        private Texture2D runtimeTexture;
        private MaterialPropertyBlock properties;
        private Color32[] sketchPixels, paintPixels, displayPixels;
        private bool[] requiredSketch, coveredSketch;
        private byte[] requiredPaint, appliedPaint;
        private readonly int[] sketchTileTargets = new int[TileCount];
        private readonly int[] sketchTileCovered = new int[TileCount];
        private readonly int[] paintTileTargets = new int[TileCount];
        private readonly int[] paintTileCovered = new int[TileCount];
        private readonly int[] colorTargets = new int[4];
        private readonly int[] colorCovered = new int[4];
        private int sketchTargetCount, sketchCoveredCount, paintTargetCount, paintCoveredCount;
        private bool initialized, initializationAttempted, workEnabled, applicationPaused, textureDirty;
        private bool sketchComplete, paintComplete;
        private float lastSketchReport, lastPaintReport;

        public event Action StateChanged;
        public string SurfaceLabel => string.IsNullOrWhiteSpace(surfaceLabel) ? gameObject.name : surfaceLabel;
        public bool IsSketchComplete => sketchComplete;
        public bool IsPaintComplete => paintComplete;
        public bool WorkEnabled => workEnabled;
        public float SketchProgress => sketchComplete ? 1f : Fraction(sketchCoveredCount, sketchTargetCount);
        public float PaintProgress => paintComplete ? 1f : Fraction(paintCoveredCount, paintTargetCount);
        public int SketchCoveredPixels => sketchCoveredCount;
        public int PaintCoveredPixels => paintCoveredCount;
        public Texture2D RuntimeTexture => runtimeTexture;
        public InteractionEventUnityEvent OnProgress => onProgress;
        public InteractionEventUnityEvent OnCompleted => onCompleted;
        public string RequiredColorName
        {
            get
            {
                for (var color = 1; color <= 3; color++)
                    if (colorTargets[color] > 0 && RawFraction(colorCovered[color], colorTargets[color]) < requiredCoverage)
                        return ColorName(color);
                if (!paintComplete && initialized)
                {
                    for (var tile = 0; tile < TileCount; tile++)
                    {
                        if (paintTileTargets[tile] == 0 ||
                            RawFraction(paintTileCovered[tile], paintTileTargets[tile]) >= requiredTileCoverage) continue;
                        var firstX = tile % 4 * (TextureSize / 4);
                        var firstY = tile / 4 * (TextureSize / 4);
                        for (var y = firstY; y < firstY + TextureSize / 4; y++)
                        for (var x = firstX; x < firstX + TextureSize / 4; x++)
                        {
                            var pixel = y * TextureSize + x;
                            if (requiredPaint[pixel] > 0 && appliedPaint[pixel] != requiredPaint[pixel])
                                return ColorName(requiredPaint[pixel]);
                        }
                    }
                }
                return paintComplete ? "已完成" : "补全尚未涂满的纹样";
            }
        }

        public void Configure(string label, MeshFilter mesh, Renderer targetRenderer, PaperPaintTool[] paintingTools,
            Texture2D tracingMask, Texture2D coloringMask)
        {
            surfaceLabel = label;
            drawingMesh = mesh;
            drawingRenderer = targetRenderer;
            tools = paintingTools ?? Array.Empty<PaperPaintTool>();
            sketchMask = tracingMask;
            paintMask = coloringMask;
        }

        public void ConfigurePalette(Color red, Color green, Color black)
        {
            redPigment = red;
            greenPigment = green;
            blackPigment = black;
            if (initialized) RebuildDisplay();
        }

        public void ConfigureAdditionalRenderers(Renderer[] renderers)
        {
            additionalRenderers = renderers ?? Array.Empty<Renderer>();
            if (initialized) BindTexture();
        }

        public void SetWorkEnabled(bool enabled)
        {
            if (workEnabled == enabled) return;
            workEnabled = enabled;
            ClearStrokeContinuity();
            if (initialized) RebuildDisplay();
        }

        /// <summary>Stops a stroke from bridging a release, pause, recovery or workpiece rotation.</summary>
        public void ClearStrokeContinuity()
        {
            if (previousSamples != null) Array.Clear(previousSamples, 0, previousSamples.Length);
        }

        /// <summary>Clears only the unfinished operation, so successful event requirements never need undoing.</summary>
        public bool ResetUnfinishedOperation()
        {
            if (!EnsureRuntime() || paintComplete) return false;
            ClearStrokeContinuity();
            if (!sketchComplete)
            {
                Array.Clear(sketchPixels, 0, sketchPixels.Length);
                Array.Clear(coveredSketch, 0, coveredSketch.Length);
                Array.Clear(sketchTileCovered, 0, sketchTileCovered.Length);
                sketchCoveredCount = 0;
                lastSketchReport = 0f;
                Report(InteractionKind.SketchProgress, InteractionResult.Progressed, 0f);
            }
            else
            {
                ClearPaint();
                Report(InteractionKind.PaintProgress, InteractionResult.Progressed, 0f);
            }
            RebuildDisplay();
            StateChanged?.Invoke();
            return true;
        }

        public void ResetInteraction()
        {
            ClearStrokeContinuity();
            workEnabled = false;
            sketchComplete = false;
            paintComplete = false;
            sketchCoveredCount = 0;
            lastSketchReport = 0f;
            if (initialized)
            {
                Array.Clear(sketchPixels, 0, sketchPixels.Length);
                Array.Clear(coveredSketch, 0, coveredSketch.Length);
                Array.Clear(sketchTileCovered, 0, sketchTileCovered.Length);
                ClearPaint();
                RebuildDisplay();
            }
            StateChanged?.Invoke();
        }

        public float GetPaintColorProgress(string color)
        {
            var code = ColorCode(color);
            return code == 0 ? 0f : Fraction(colorCovered[code], colorTargets[code]);
        }

        private void Awake()
        {
            identity = GetComponent<InteractableIdentity>();
            gate = GetComponent<InteractionGate>();
            EnsureRuntime();
        }

        private void FixedUpdate()
        {
            if (!workEnabled || paintComplete || applicationPaused || !Application.isFocused ||
                !EnsureRuntime() || drawingRenderer == null || !drawingRenderer.enabled ||
                !drawingRenderer.gameObject.activeInHierarchy)
            {
                ClearStrokeContinuity();
                return;
            }
            var operation = sketchComplete ? InteractionKind.PaintProgress : InteractionKind.SketchProgress;
            if (gate == null || !gate.CanPerform(operation, reportDenial: false))
            {
                ClearStrokeContinuity();
                return;
            }

            var changed = false;
            for (var index = 0; index < tools.Length; index++)
            {
                var tool = tools[index];
                ref var previous = ref previousSamples[index];
                if (tool == null || !tool.IsDrawing || tool.IsPencil == sketchComplete ||
                    (!tool.IsPencil && !tool.HasPaint) ||
                    !TryProject(tool.WorkingPosition, out var uv, out var position, out var tangentU, out var tangentV))
                {
                    previous.Active = false;
                    continue;
                }

                var strokeColor = tool.IsPencil ? string.Empty : tool.ColorId;
                var distance = previous.Active ? Vector3.Distance(previous.Position, position) : 0f;
                var canConnect = previous.Active && previous.Color == strokeColor &&
                    Time.fixedTime - previous.Time <= 0.12f && distance <= 0.035f &&
                    Vector2.Distance(previous.Uv, uv) <= 0.2f;
                var sampleCount = canConnect ? Mathf.Clamp(Mathf.CeilToInt(distance / (tool.RadiusMetres * 0.5f)), 1, 16) : 1;
                for (var sample = 1; sample <= sampleCount; sample++)
                {
                    var sampleUv = canConnect ? Vector2.Lerp(previous.Uv, uv, sample / (float)sampleCount) : uv;
                    changed |= Stamp(sampleUv, tangentU, tangentV, tool);
                }
                previous.Active = true;
                previous.Uv = uv;
                previous.Position = position;
                previous.Time = Time.fixedTime;
                previous.Color = strokeColor;
            }
            if (!changed) return;
            UpdateProgressAndCompletion();
            StateChanged?.Invoke();
        }

        private void LateUpdate()
        {
            if (!textureDirty || runtimeTexture == null) return;
            runtimeTexture.SetPixels32(displayPixels);
            runtimeTexture.Apply(false, false);
            textureDirty = false;
        }

        private bool Stamp(Vector2 uv, Vector3 tangentU, Vector3 tangentV, PaperPaintTool tool)
        {
            // These derivatives map one UV unit to metres on this actual mesh triangle.
            var determinant = Vector3.Cross(tangentU, tangentV).sqrMagnitude;
            if (determinant < 0.0000000001f) return false;
            var radius = tool.RadiusMetres;
            var extentU = radius * Mathf.Sqrt(tangentV.sqrMagnitude / determinant);
            var extentV = radius * Mathf.Sqrt(tangentU.sqrMagnitude / determinant);
            var minX = Mathf.Clamp(Mathf.FloorToInt((uv.x - extentU) * TextureSize), 0, TextureSize - 1);
            var maxX = Mathf.Clamp(Mathf.CeilToInt((uv.x + extentU) * TextureSize), 0, TextureSize - 1);
            var minY = Mathf.Clamp(Mathf.FloorToInt((uv.y - extentV) * TextureSize), 0, TextureSize - 1);
            var maxY = Mathf.Clamp(Mathf.CeilToInt((uv.y + extentV) * TextureSize), 0, TextureSize - 1);
            var squaredRadius = radius * radius;
            var code = ColorCode(tool.ColorId);
            var color = tool.IsPencil ? PencilColor : (Color32)tool.PaintColor;
            var changed = false;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var offset = tangentU * ((x + 0.5f) / TextureSize - uv.x) +
                    tangentV * ((y + 0.5f) / TextureSize - uv.y);
                if (offset.sqrMagnitude > squaredRadius) continue;
                var pixel = y * TextureSize + x;
                var tile = TileIndex(x, y);
                if (tool.IsPencil)
                {
                    if (sketchPixels[pixel].a > 0) continue;
                    sketchPixels[pixel] = color;
                    if (requiredSketch[pixel] && !coveredSketch[pixel])
                    {
                        coveredSketch[pixel] = true;
                        sketchCoveredCount++;
                        sketchTileCovered[tile]++;
                    }
                }
                else
                {
                    // Reserved paper remains unpainted. Inside a color area a wrong color stays visible until corrected.
                    var existing = paintPixels[pixel];
                    if (requiredPaint[pixel] == 0 || code == 0 ||
                        (appliedPaint[pixel] == code && existing.r == color.r && existing.g == color.g &&
                         existing.b == color.b && existing.a == color.a)) continue;
                    if (appliedPaint[pixel] == requiredPaint[pixel])
                    {
                        paintCoveredCount--;
                        colorCovered[requiredPaint[pixel]]--;
                        paintTileCovered[tile]--;
                    }
                    appliedPaint[pixel] = code;
                    paintPixels[pixel] = color;
                    if (code == requiredPaint[pixel])
                    {
                        paintCoveredCount++;
                        colorCovered[code]++;
                        paintTileCovered[tile]++;
                    }
                }
                ComposePixel(pixel);
                changed = true;
            }
            textureDirty |= changed;
            return changed;
        }

        private void UpdateProgressAndCompletion()
        {
            if (!sketchComplete)
            {
                var completed = sketchTargetCount > 0 &&
                    RawFraction(sketchCoveredCount, sketchTargetCount) >= requiredCoverage &&
                    TilesCovered(sketchTileCovered, sketchTileTargets);
                if (completed || Mathf.Abs(SketchProgress - lastSketchReport) >= 0.02f)
                {
                    lastSketchReport = SketchProgress;
                    Report(InteractionKind.SketchProgress, InteractionResult.Progressed, completed ? 1f : SketchProgress);
                }
                if (!completed) return;
                sketchComplete = true;
                ClearStrokeContinuity();
                RebuildDisplay();
                Report(InteractionKind.SketchComplete, InteractionResult.Succeeded, 1f);
                return;
            }

            var allColors = paintTargetCount > 0;
            for (var color = 1; color <= 3; color++)
                if (colorTargets[color] > 0 && RawFraction(colorCovered[color], colorTargets[color]) < requiredCoverage)
                    allColors = false;
            var paintingCompleted = allColors && TilesCovered(paintTileCovered, paintTileTargets);
            if (paintingCompleted || Mathf.Abs(PaintProgress - lastPaintReport) >= 0.02f)
            {
                lastPaintReport = PaintProgress;
                Report(InteractionKind.PaintProgress, InteractionResult.Progressed, paintingCompleted ? 1f : PaintProgress);
            }
            if (!paintingCompleted) return;
            paintComplete = true;
            ClearStrokeContinuity();
            RebuildDisplay();
            Report(InteractionKind.PaintComplete, InteractionResult.Succeeded, 1f);
        }

        private bool TilesCovered(int[] covered, int[] targets)
        {
            for (var tile = 0; tile < TileCount; tile++)
                if (targets[tile] > 0 && RawFraction(covered[tile], targets[tile]) < requiredTileCoverage) return false;
            return true;
        }

        private bool EnsureRuntime()
        {
            if (initialized) return true;
            if (!Application.isPlaying || initializationAttempted) return false;
            initializationAttempted = true;
            if (drawingMesh == null || drawingRenderer == null || drawingMesh.sharedMesh == null ||
                !drawingMesh.sharedMesh.isReadable || sketchMask == null || paintMask == null ||
                !sketchMask.isReadable || !paintMask.isReadable || sketchMask.width != TextureSize ||
                sketchMask.height != TextureSize || paintMask.width != TextureSize || paintMask.height != TextureSize)
            {
                Debug.LogError($"[FishLanternXR] {name}: 绘画网格必须可读，两张遮罩必须是可读的 512 × 512 贴图。", this);
                return false;
            }

            var mesh = drawingMesh.sharedMesh;
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var indices = mesh.triangles;
            if (uvs.Length != vertices.Length || indices.Length == 0)
            {
                Debug.LogError($"[FishLanternXR] {name}: 绘画网格缺少独立 UV 或三角形。", this);
                return false;
            }
            triangles = new Triangle[indices.Length / 3];
            for (var index = 0; index < triangles.Length; index++)
            {
                var a = indices[index * 3];
                var b = indices[index * 3 + 1];
                var c = indices[index * 3 + 2];
                var firstUv = uvs[b] - uvs[a];
                var secondUv = uvs[c] - uvs[a];
                var determinant = firstUv.x * secondUv.y - firstUv.y * secondUv.x;
                var firstEdge = vertices[b] - vertices[a];
                var secondEdge = vertices[c] - vertices[a];
                triangles[index] = new Triangle
                {
                    A = vertices[a], B = vertices[b], C = vertices[c],
                    UvA = uvs[a], UvB = uvs[b], UvC = uvs[c],
                    TangentU = Mathf.Abs(determinant) > 0.0000001f ?
                        (firstEdge * secondUv.y - secondEdge * firstUv.y) / determinant : Vector3.zero,
                    TangentV = Mathf.Abs(determinant) > 0.0000001f ?
                        (secondEdge * firstUv.x - firstEdge * secondUv.x) / determinant : Vector3.zero,
                };
            }
            var pixelCount = TextureSize * TextureSize;
            sketchPixels = new Color32[pixelCount];
            paintPixels = new Color32[pixelCount];
            displayPixels = new Color32[pixelCount];
            requiredSketch = new bool[pixelCount];
            coveredSketch = new bool[pixelCount];
            requiredPaint = new byte[pixelCount];
            appliedPaint = new byte[pixelCount];
            previousSamples = new StrokeSample[tools.Length];
            var sketchMaskPixels = sketchMask.GetPixels32();
            var paintMaskPixels = paintMask.GetPixels32();
            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                var tile = TileIndex(pixel % TextureSize, pixel / TextureSize);
                if (sketchMaskPixels[pixel].a > 127)
                {
                    requiredSketch[pixel] = true;
                    sketchTargetCount++;
                    sketchTileTargets[tile]++;
                }
                var mask = paintMaskPixels[pixel];
                if (mask.a <= 127) continue;
                var code = mask.r >= mask.g && mask.r >= mask.b ? 1 : mask.g >= mask.b ? 2 : 3;
                requiredPaint[pixel] = (byte)code;
                paintTargetCount++;
                paintTileTargets[tile]++;
                colorTargets[code]++;
            }
            if (sketchTargetCount == 0 || paintTargetCount == 0)
                Debug.LogError($"[FishLanternXR] {name}: 描线和上色遮罩都必须包含实际目标区域。", this);
            runtimeTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, false)
            {
                name = $"{name} 本轮独立笔触", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            properties = new MaterialPropertyBlock();
            initialized = true;
            BindTexture();
            RebuildDisplay();
            return true;
        }

        private bool TryProject(Vector3 worldPoint, out Vector2 uv, out Vector3 position,
            out Vector3 tangentU, out Vector3 tangentV)
        {
            uv = default;
            position = default;
            tangentU = default;
            tangentV = default;
            var maxDistance = contactDistance * contactDistance;
            if (drawingRenderer.bounds.SqrDistance(worldPoint) > maxDistance) return false;
            var meshTransform = drawingMesh.transform;
            var localPoint = meshTransform.InverseTransformPoint(worldPoint);
            var closestDistance = maxDistance;
            var found = false;
            foreach (var triangle in triangles)
            {
                if (triangle.TangentU.sqrMagnitude < 0.0000001f || triangle.TangentV.sqrMagnitude < 0.0000001f) continue;
                var barycentric = ClosestBarycentric(localPoint, triangle.A, triangle.B, triangle.C);
                var localPosition = triangle.A * barycentric.x + triangle.B * barycentric.y + triangle.C * barycentric.z;
                var worldPosition = meshTransform.TransformPoint(localPosition);
                var distance = (worldPosition - worldPoint).sqrMagnitude;
                if (distance > closestDistance) continue;
                closestDistance = distance;
                position = worldPosition;
                uv = triangle.UvA * barycentric.x + triangle.UvB * barycentric.y + triangle.UvC * barycentric.z;
                tangentU = meshTransform.TransformVector(triangle.TangentU);
                tangentV = meshTransform.TransformVector(triangle.TangentV);
                found = true;
            }
            return found;
        }

        private static Vector3 ClosestBarycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = point - a;
            var d1 = Vector3.Dot(ab, ap);
            var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);
            var bp = point - b;
            var d3 = Vector3.Dot(ab, bp);
            var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                var v = d1 / (d1 - d3);
                return new Vector3(1f - v, v, 0f);
            }
            var cp = point - c;
            var d5 = Vector3.Dot(ab, cp);
            var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                var w = d2 / (d2 - d6);
                return new Vector3(1f - w, 0f, w);
            }
            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return new Vector3(0f, 1f - w, w);
            }
            var denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 0.000000000000000001f) return new Vector3(1f, 0f, 0f);
            var inverse = 1f / denominator;
            return new Vector3(va * inverse, vb * inverse, vc * inverse);
        }

        private void ClearPaint()
        {
            Array.Clear(paintPixels, 0, paintPixels.Length);
            Array.Clear(appliedPaint, 0, appliedPaint.Length);
            Array.Clear(paintTileCovered, 0, paintTileCovered.Length);
            Array.Clear(colorCovered, 0, colorCovered.Length);
            paintCoveredCount = 0;
            lastPaintReport = 0f;
        }

        private void RebuildDisplay()
        {
            if (!initialized) return;
            for (var pixel = 0; pixel < displayPixels.Length; pixel++) ComposePixel(pixel);
            textureDirty = true;
        }

        private void ComposePixel(int pixel)
        {
            var painted = paintPixels[pixel];
            var pencil = sketchPixels[pixel];
            if (painted.a > 0)
            {
                // Color may cover the visible pencil, but the completed sketch statistics remain intact.
                displayPixels[pixel] = painted;
                return;
            }
            if (pencil.a > 0)
            {
                displayPixels[pixel] = pencil;
                return;
            }
            if (workEnabled && !paintComplete)
            {
                if (!sketchComplete && requiredSketch[pixel])
                {
                    displayPixels[pixel] = new Color32(65, 58, 50, 33);
                    return;
                }
                if (sketchComplete && requiredPaint[pixel] > 0)
                {
                    var color = (Color32)Pigment(requiredPaint[pixel]);
                    color.a = 25;
                    displayPixels[pixel] = color;
                    return;
                }
            }
            displayPixels[pixel] = default;
        }

        private void BindTexture()
        {
            BindRenderer(drawingRenderer);
            foreach (var renderer in additionalRenderers) BindRenderer(renderer);
        }

        private void BindRenderer(Renderer renderer)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, runtimeTexture);
            properties.SetTexture(MainTextureId, runtimeTexture);
            properties.SetColor(BaseColorId, Color.white);
            properties.SetColor(ColorId, Color.white);
            renderer.SetPropertyBlock(properties);
        }

        private void Report(InteractionKind kind, InteractionResult result, float progress)
        {
            if (identity == null) identity = GetComponent<InteractableIdentity>();
            var data = identity.CreateEvent(kind, result, progress: progress);
            InteractionServiceHub.Active?.Report(data);
            if (result == InteractionResult.Succeeded) onCompleted.Invoke(data);
            else onProgress.Invoke(data);
        }

        private void OnDisable() => ClearStrokeContinuity();
        private void OnApplicationFocus(bool focused) { if (!focused) ClearStrokeContinuity(); }
        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) ClearStrokeContinuity();
        }
        private void OnDestroy() { if (runtimeTexture != null) Destroy(runtimeTexture); }
        private float Fraction(int covered, int target) => target <= 0 ? 0f : Mathf.Min(0.999f, covered / (target * requiredCoverage));
        private static float RawFraction(int covered, int target) => target <= 0 ? 0f : covered / (float)target;
        private static int TileIndex(int x, int y) => (y * 4 / TextureSize) * 4 + x * 4 / TextureSize;
        private static byte ColorCode(string color) => color switch { "red" => 1, "green" => 2, "black" => 3, _ => 0 };
        private static string ColorName(int code) => code switch { 1 => "朱红", 2 => "石绿", 3 => "墨黑", _ => "尚未蘸色" };
        private Color Pigment(int code) => code switch { 1 => redPigment, 2 => greenPigment, 3 => blackPigment, _ => Color.clear };
    }
}
