using System.Collections.Generic;
using UnityEngine;

// Pasang di objek halaman (mis. Page.001). Menghitung Tiling/Offset otomatis supaya
// SELURUH gambar Base Map pas di satu muka halaman. Berlaku juga di Scene view (tanpa Play).
// MonocleLens memakai komponen ini untuk tahu di mana muka halaman berada.
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
        public float bestTriArea = -1f;
        public int ta = -1, tb, tc;     // segitiga acuan terbesar di muka ini
    }

    // hasil hitungan terakhir
    private Vector4 st = new Vector4(1f, 1f, 0f, 0f);
    private int refA = -1, refB, refC;
    private Vector2[] cachedUV;
    private Vector3[] staticVerts;
    private Mesh baked;
    private bool ready;

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
            float triArea = Mathf.Abs(d1.x * d2.y - d1.y * d2.x) * 0.5f;
            isl.area += triArea;
            if (triArea > 1e-9f && triArea > isl.bestTriArea)
            {
                isl.bestTriArea = triArea;
                isl.ta = tr[i]; isl.tb = tr[i + 1]; isl.tc = tr[i + 2];
            }
        }

        List<Island> list = new List<Island>(map.Values);
        list.Sort((p, q) => q.area.CompareTo(p.area));
        return list;
    }

    [ContextMenu("Fit Texture Now")]
    public void Apply()
    {
        ready = false;
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

        int idx = Mathf.Clamp(pickIsland, 0, islands.Count - 1);
        Island chosen = useWholeMesh ? islands[0] : islands[idx];

        Vector2 min, max;
        if (useWholeMesh)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (Island i in islands) { min = Vector2.Min(min, i.min); max = Vector2.Max(max, i.max); }
        }
        else
        {
            min = chosen.min;
            max = chosen.max;
        }

        Vector2 size = max - min;
        if (size.x < 1e-5f || size.y < 1e-5f || chosen.ta < 0) return;

        float sx = 1f / size.x, sy = 1f / size.y;
        float ox = -min.x * sx, oy = -min.y * sy;
        if (flipX) { sx = -sx; ox = 1f - ox; }
        if (flipY) { sy = -sy; oy = 1f - oy; }

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb, materialIndex);
        mpb.SetVector(BaseMapST, new Vector4(sx, sy, ox, oy));
        r.SetPropertyBlock(mpb, materialIndex);

        // simpan untuk GetFaceWorldCorners
        st = new Vector4(sx, sy, ox, oy);
        refA = chosen.ta; refB = chosen.tb; refC = chosen.tc;
        cachedUV = m.uv;
        staticVerts = (r is SkinnedMeshRenderer) ? null : m.vertices;
        ready = true;

        if (debugLog)
        {
            string list = "";
            for (int i = 0; i < Mathf.Min(islands.Count, 6); i++)
                list += $"\n  #{i}: luas={islands[i].area:F4} min={islands[i].min} max={islands[i].max}";
            Debug.Log($"[PageUVFit] {name}: {islands.Count} muka UV. Dipakai: {(useWholeMesh ? "seluruh mesh" : "#" + idx)}. Tiling=({sx:F3},{sy:F3}) Offset=({ox:F3},{oy:F3}){list}");
        }
    }

    // Empat sudut muka halaman dalam koordinat dunia, urutan sudut texture:
    // [0]=(0,0) [1]=(1,0) [2]=(0,1) [3]=(1,1). Sudut ini mengikuti pemetaan texture di halaman,
    // jadi foto lain yang digambar dengan UV 0..1 pada sudut-sudut ini akan sejajar dengan gambar halaman.
    public bool GetFaceWorldCorners(Vector3[] result)
    {
        if (result == null || result.Length < 4) return false;
        if (!ready) Apply();
        if (!ready || refA < 0 || cachedUV == null) return false;

        Renderer r = GetComponent<Renderer>();
        Vector3[] v;
        SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
        if (sk != null)
        {
            if (baked == null) baked = new Mesh();
            sk.BakeMesh(baked, false);
            v = baked.vertices;
        }
        else v = staticVerts;

        if (v == null || v.Length != cachedUV.Length) return false;

        Vector3 a = v[refA], b = v[refB], c = v[refC];
        Vector2 ta = cachedUV[refA], tb = cachedUV[refB], tc = cachedUV[refC];
        Vector2 d1 = tb - ta, d2 = tc - ta;
        float det = d1.x * d2.y - d1.y * d2.x;
        if (Mathf.Abs(det) < 1e-12f) return false;

        for (int k = 0; k < 4; k++)
        {
            float tu = (k & 1), tv = (k >> 1);
            Vector2 uv = new Vector2((tu - st.z) / st.x, (tv - st.w) / st.y);
            Vector2 w = uv - ta;
            float s = (w.x * d2.y - w.y * d2.x) / det;
            float q = (d1.x * w.y - d1.y * w.x) / det;
            Vector3 local = a + s * (b - a) + q * (c - a);
            result[k] = transform.TransformPoint(local);
        }
        return true;
    }

    // Seberapa menghadap kamera muka halaman ini. Tanda (+/-) tergantung arah mesh, jadi
    // MonocleLens mengkalibrasinya saat buku terbuka. Mendekati 0 = halaman menyamping
    // (buku sedang menutup atau membuka).
    public bool GetFacing(Vector3 camPos, out float facing)
    {
        facing = 1f;
        if (!ready) Apply();
        if (!ready || refA < 0 || cachedUV == null) return false;

        Renderer r = GetComponent<Renderer>();
        Vector3[] v;
        SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
        if (sk != null)
        {
            if (baked == null) baked = new Mesh();
            sk.BakeMesh(baked, false);
            v = baked.vertices;
        }
        else v = staticVerts;

        if (v == null || v.Length != cachedUV.Length) return false;

        Vector3 a = v[refA], b = v[refB], c = v[refC];
        Vector3 nWorld = transform.TransformDirection(Vector3.Cross(b - a, c - a)).normalized;
        Vector3 center = transform.TransformPoint((a + b + c) / 3f);
        facing = Vector3.Dot(nWorld, (camPos - center).normalized);
        return true;
    }
}