using System.Collections.Generic;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// A found creature. Its body is built from the exact outline the player saw in depth (the
    /// stereogram mask's radius, wobble, lobes and seed), so what comes out of the static is the shape
    /// they found. One big eye follows the player's head and blinks. No colliders: it never blocks a pick.
    /// Local frame: X and Z are the surface (patch +u and +v), Y is up out of the surface.
    /// </summary>
    public sealed class CreatureBody : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int ShimmerId = Shader.PropertyToID("_Shimmer");
        static readonly int RimId = Shader.PropertyToID("_Rim");

        public Creatures.Species Species { get; private set; }
        public Creatures.Rarity Rarity { get; private set; }
        public float RadiusM { get; private set; }
        public float HeightM { get; private set; }
        public float Wobble { get; private set; }
        public float Lobes { get; private set; }
        public float Seed { get; private set; }

        [Tooltip("What the eye follows (the player's head).")]
        public Transform Look;
        [Range(0, 1)] public float Dissolve;
        [Tooltip("Squash and stretch: + flattens (landing), - stretches (take-off).")]
        public float Squash;

        Transform body, eye, pupil;
        Renderer bodyR, eyeR, pupilR;
        MaterialPropertyBlock mpb;
        Mesh mesh;
        Color color;
        float rim, shimmer, nextBlink, blinkUntil, eyeSize;

        public static CreatureBody Create(Material mat, Transform parent, float radiusM, float wobble, float lobes, float seed,
                                          Creatures.Rarity rarity)
        {
            var root = new GameObject("Creature").transform;
            root.SetParent(parent, false);
            var c = root.gameObject.AddComponent<CreatureBody>();
            c.Build(mat, radiusM, wobble, lobes, seed, rarity);
            return c;
        }

        void Build(Material mat, float radiusM, float wobble, float lobes, float seed, Creatures.Rarity rarity)
        {
            RadiusM = radiusM; Wobble = wobble; Lobes = lobes; Seed = seed;
            Species = Creatures.FromLobes(lobes);
            Rarity = rarity;
            HeightM = 0.62f * radiusM;
            color = ColorFor(Species, rarity);
            rim = rarity >= Creatures.Rarity.Rare ? 0.45f : 0.15f;
            shimmer = rarity == Creatures.Rarity.Elusive ? 1f : 0f;
            mpb = new MaterialPropertyBlock();

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            mesh = BuildMesh(radiusM, wobble, lobes, seed);
            body.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            bodyR = body.gameObject.AddComponent<MeshRenderer>();
            bodyR.sharedMaterial = mat;

            eyeSize = 0.68f * radiusM;
            eye = Part("Eye", body, mat, new Vector3(0f, HeightM * 0.86f, 0f), eyeSize, out eyeR);
            pupil = Part("Pupil", eye, mat, new Vector3(0f, 0f, 0.31f), 0.55f, out pupilR);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            nextBlink = Time.time + Random.Range(1.5f, 3.5f);
            Apply();
        }

        static Transform Part(string name, Transform parent, Material mat, Vector3 localPos, float size, out Renderer r)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * size;
            r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>
        /// Dome whose footprint is the mask outline: rings rise from the outline at the base to a round
        /// top, so the lobes read as stubby feet. Seam-free (rings wrap), base cap separate.
        /// </summary>
        public static Mesh BuildMesh(float radiusM, float wobble, float lobes, float seed, int segments = 48, int rings = 6)
        {
            float h = 0.62f * radiusM;
            var v = new List<Vector3>(segments * (rings + 1) + 2);
            var t = new List<int>(segments * rings * 6 + segments * 6);
            for (int k = 0; k < rings; k++)
            {
                float phi = k / (float)rings * Mathf.PI * 0.5f;
                float s = Mathf.Pow(Mathf.Cos(phi), 0.6f);
                float y = h * Mathf.Sin(phi);
                float blend = k / (float)rings * 0.7f;
                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments * 2f * Mathf.PI;
                    float outline = (float)Creatures.OutlineRadius(radiusM, wobble, lobes, seed, a);
                    float r = s * Mathf.Lerp(outline, radiusM, blend);
                    v.Add(new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a)));
                }
            }
            int top = v.Count;
            v.Add(new Vector3(0f, h, 0f));
            for (int k = 0; k < rings - 1; k++)
                for (int i = 0; i < segments; i++)
                {
                    int i1 = (i + 1) % segments;
                    int a0 = k * segments + i, a1 = k * segments + i1, b0 = a0 + segments, b1 = a1 + segments;
                    t.Add(a0); t.Add(b0); t.Add(a1);
                    t.Add(a1); t.Add(b0); t.Add(b1);
                }
            int last = (rings - 1) * segments;
            for (int i = 0; i < segments; i++) { t.Add(last + i); t.Add(top); t.Add(last + (i + 1) % segments); }
            int baseStart = v.Count;
            for (int i = 0; i < segments; i++) v.Add(v[i]);
            int baseCenter = v.Count;
            v.Add(Vector3.zero);
            for (int i = 0; i < segments; i++) { t.Add(baseStart + i); t.Add(baseStart + (i + 1) % segments); t.Add(baseCenter); }

            var m = new Mesh { name = "CreatureBody" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static Color ColorFor(Creatures.Species s, Creatures.Rarity r)
        {
            float hue = s switch
            {
                Creatures.Species.Trifoil => 0.48f,   // teal
                Creatures.Species.Clover => 0.24f,    // leaf green
                Creatures.Species.Starfin => 0.03f,   // coral
                _ => 0.77f                            // violet
            };
            float sat = r switch
            {
                Creatures.Rarity.Common => 0.35f,
                Creatures.Rarity.Uncommon => 0.55f,
                Creatures.Rarity.Rare => 0.8f,
                _ => 0.65f
            };
            return Color.HSVToRGB(hue, sat, 0.95f);
        }

        void LateUpdate() => Refresh();

        /// <summary>Applies squash, blink, gaze and colours (each frame; also callable from editor captures).</summary>
        public void Refresh()
        {
            // Squash and stretch, volume-preserving-ish.
            float q = Mathf.Clamp(Squash, -0.6f, 0.6f);
            body.localScale = new Vector3(1f + 0.3f * q, 1f - 0.45f * q, 1f + 0.3f * q);

            // Blink now and then.
            if (Time.time >= nextBlink)
            {
                blinkUntil = Time.time + 0.12f;
                nextBlink = Time.time + Random.Range(2f, 4.5f);
            }
            float lid = Time.time < blinkUntil ? 0.12f : 1f;
            eye.localScale = new Vector3(eyeSize, eyeSize * lid, eyeSize);

            // Pupil looks at the player.
            if (Look != null)
            {
                // Rotation only; the eye's own (blink) scale then keeps the pupil on its surface.
                Vector3 dir = eye.InverseTransformDirection((Look.position - eye.position).normalized);
                pupil.localPosition = dir.sqrMagnitude > 0f ? dir * 0.31f : new Vector3(0f, 0f, 0.31f);
            }
            Apply();
        }

        void Apply()
        {
            Set(bodyR, color);
            Set(eyeR, new Color(0.97f, 0.97f, 0.95f));
            Set(pupilR, new Color(0.05f, 0.05f, 0.08f));
        }

        void Set(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, c);
            mpb.SetFloat(DissolveId, Dissolve);
            mpb.SetFloat(ShimmerId, r == bodyR ? shimmer : 0f);
            mpb.SetFloat(RimId, r == bodyR ? rim : 0f);
            r.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
