using System.Collections.Generic;
using UnityEngine;

// Pasang di objek halaman (mis. Page.001). Menghitung Tiling/Offset otomatis supaya
// SELURUH gambar Base Map pas di satu muka halaman. Berlaku juga di Scene view (tanpa Play).
[ExecuteAlways]
public class PageUVFit : MonoBehaviour
{
    [Tooltip("0 = muka UV terbesar, 1 = terbesar kedua, dst. Ganti sampai seluruh gambar pas di halaman.")]
    public int pickIsland = 0;
    [Tooltip("Pakai seluruh UV mesh (bukan satu muka saja)")]
    public bool useWholeMesh = false;
    public bool flipX;
    public bool flipY;
    public int materialIndex = 0;
    public bool debugLog = true;

    static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

    class Island
    {
        public float area;
        public Vector2 min, max;
    }

    void OnEnable() { Apply(); }
    void OnValidate() { Apply(); }

    void OnDisable()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null) r.SetPropertyBlock(null, materialIndex);
    }

    Mesh GetMesh(Renderer r)
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
        SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
        return sk != null ? sk.sharedMesh : null;
    }

    // Kelompokkan segitiga yang saling tersambung menjadi "pulau" UV (satu muka = satu pulau).
    List<Island> FindIslands(Mesh m)
    {
        Vector2[] uv = m.uv;
        int[] tr = m.triangles;
        int n = m.vertexCount;
        if (uv == null || uv.Length != n || tr.Length < 3) return null;

        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        for (int i = 0; i + 2 < tr.Length; i += 3)
        {
            Union(tr[i], tr[i + 1]);
            Union(tr[i], tr[i + 2]);
        }

        Dictionary<int, Island> map = new Dictionary<int, Island>();
        for (int i = 0; i + 2 < tr.Length; i += 3)
        {
            int root = Find(tr[i]);
            Island isl;
            if (!map.TryGetValue(root, out isl))
            {
                isl = new Island { min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue) };
                map[root] = isl;
            }

            Vector2 a = uv[tr[i]], b = uv[tr[i + 1]], c = uv[tr[i + 2]];
            isl.min = Vector2.Min(isl.min, Vector2.Min(a, Vector2.Min(b, c)));
            isl.max = Vector2.Max(isl.max, Vector2.Max(a, Vector2.Max(b, c)));
            Vector2 d1 = b - a, d2 = c - a;
            isl.area += Mathf.Abs(d1.x * d2.y - d1.y * d2.x) * 0.5f;
        }

        List<Island> list = new List<Island>(map.Values);
        list.Sort((p, q) => q.area.CompareTo(p.area));
        return list;
    }

    [ContextMenu("Fit Texture Now")]
    public void Apply()
    {
        Renderer r = GetComponent<Renderer>();
        if (r == null) return;
        Mesh m = GetMesh(r);
        if (m == null) return;

        List<Island> islands = FindIslands(m);
        if (islands == null || islands.Count == 0)
        {
            Debug.LogWarning($"[PageUVFit] {name}: mesh tidak punya UV yang valid (coba centang Read/Write di import model)");
            return;
        }

        Vector2 min, max;
        if (useWholeMesh)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (Island i in islands) { min = Vector2.Min(min, i.min); max = Vector2.Max(max, i.max); }
        }
        else
        {
            int idx = Mathf.Clamp(pickIsland, 0, islands.Count - 1);
            min = islands[idx].min;
            max = islands[idx].max;
        }

        Vector2 size = max - min;
        if (size.x < 1e-5f || size.y < 1e-5f) return;

        float sx = 1f / size.x, sy = 1f / size.y;
        float ox = -min.x * sx, oy = -min.y * sy;
        if (flipX) { sx = -sx; ox = 1f - ox; }
        if (flipY) { sy = -sy; oy = 1f - oy; }

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb, materialIndex);
        mpb.SetVector(BaseMapST, new Vector4(sx, sy, ox, oy));
        r.SetPropertyBlock(mpb, materialIndex);

        if (debugLog)
        {
            string list = "";
            for (int i = 0; i < Mathf.Min(islands.Count, 6); i++)
                list += $"\n  #{i}: luas={islands[i].area:F4} min={islands[i].min} max={islands[i].max}";
            Debug.Log($"[PageUVFit] {name}: {islands.Count} muka UV. Dipakai: {(useWholeMesh ? "seluruh mesh" : "#" + Mathf.Clamp(pickIsland, 0, islands.Count - 1))}. Tiling=({sx:F3},{sy:F3}) Offset=({ox:F3},{oy:F3}){list}");
        }
    }
}