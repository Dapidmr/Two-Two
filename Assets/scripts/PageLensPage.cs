using UnityEngine;

public class PageLensPage : MonoBehaviour
{
    public Material lensMaterial;
    public Texture gibberish;
    public Texture readable;
    public float aspect = 0.71f;
    public int monocleId = 1;

    [Header("Penyesuaian texture")]
    public bool autoFitUV = true;
    public Vector2 texTiling = Vector2.one;
    public Vector2 texOffset = Vector2.zero;

    private Material mat;
    private MeshCollider col;
    private SkinnedMeshRenderer skin;
    private Mesh baked;
    private int bakedFrame = -1;
    private Vector2 uvSize = Vector2.one;

    private bool mapReady = false;
    private Vector3 P0, e1, e2, planeN;
    private Vector2 T0, dT1, dT2;
    private float worldHeight = 1f;

    void Awake() { Init(); }

    Mesh GetMesh()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
        return skin != null ? skin.sharedMesh : null;
    }

    void FitUV()
    {
        Mesh m = GetMesh();
        if (m == null) return;
        Vector2[] uvs = m.uv;
        if (uvs == null || uvs.Length == 0) return;

        Vector2 min = uvs[0], max = uvs[0];
        foreach (Vector2 u in uvs) { min = Vector2.Min(min, u); max = Vector2.Max(max, u); }
        Vector2 size = max - min;
        if (size.x < 1e-5f || size.y < 1e-5f) return;
        uvSize = size;

        texTiling = new Vector2(1f / size.x, 1f / size.y);
        texOffset = new Vector2(-min.x / size.x, -min.y / size.y);
    }

    void Init()
    {
        if (mat != null) return;

        skin = GetComponent<SkinnedMeshRenderer>();

        mat = new Material(lensMaterial);
        foreach (var n in new[] { "_GibberishTex", "_ReadableTex", "_Aspect", "_LensUV", "_LensRadius", "_LensOn" })
            if (!mat.HasProperty(n)) Debug.LogError($"[{name}] shader tidak punya property {n}");

        if (gibberish == null || readable == null)
            Debug.LogError($"[{name}] texture gibberish/readable belum di-assign");

        mat.SetTexture("_GibberishTex", gibberish);
        mat.SetTexture("_ReadableTex", readable);
        mat.SetFloat("_Aspect", aspect);
        mat.SetFloat("_LensOn", 0f);

        if (autoFitUV) FitUV();
        else
        {
            Mesh m = GetMesh();
            if (m != null)
            {
                Vector2[] uvs = m.uv;
                if (uvs != null && uvs.Length > 0)
                {
                    Vector2 mn = uvs[0], mx = uvs[0];
                    foreach (Vector2 u in uvs) { mn = Vector2.Min(mn, u); mx = Vector2.Max(mx, u); }
                    uvSize = mx - mn;
                }
            }
        }
        mat.SetVector("_TexST", new Vector4(texTiling.x, texTiling.y, texOffset.x, texOffset.y));

        GetComponent<Renderer>().material = mat;
    }

    // Ambil bentuk mesh yang sedang tampil (sekali per frame)
    void RefreshMap()
    {
        if (bakedFrame == Time.frameCount) return;
        bakedFrame = Time.frameCount;
        mapReady = false;

        Mesh m;
        if (skin != null)
        {
            if (baked == null) baked = new Mesh();
            skin.BakeMesh(baked, false);
            m = baked;
        }
        else m = GetMesh();
        if (m == null) return;

        Vector3[] v = m.vertices;
        Vector2[] uvs = GetMesh().uv;
        int[] tr = m.triangles;
        if (uvs.Length != v.Length) return;

        Vector3 s = Vector3.Scale(m.bounds.size, transform.lossyScale);
        worldHeight = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        for (int i = 0; i + 2 < tr.Length; i += 3)
        {
            Vector3 a = v[tr[i]], b = v[tr[i + 1]], c = v[tr[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) continue;

            Vector2 ta = uvs[tr[i]], tb = uvs[tr[i + 1]], tc = uvs[tr[i + 2]];
            float uvArea = (tb - ta).x * (tc - ta).y - (tb - ta).y * (tc - ta).x;
            if (Mathf.Abs(uvArea) < 1e-10f) continue;

            P0 = a; e1 = b - a; e2 = c - a; planeN = n.normalized;
            T0 = ta; dT1 = tb - ta; dT2 = tc - ta;
            mapReady = true;
            return;
        }
    }

    public void SetCollider(bool on)
    {
        if (col == null) col = GetComponent<MeshCollider>();
        if (col == null && on) col = gameObject.AddComponent<MeshCollider>();
        if (col != null) col.enabled = on;
    }

    public void SetLens(Vector2 uv, float radius, bool on)
    {
        Init();
        mat.SetVector("_LensUV", new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_LensRadius", radius);
        mat.SetFloat("_LensOn", on ? 1f : 0f);
    }

    // Tinggi halaman di dunia dibagi rentang UV = ukuran 1 satuan UV di dunia
    public float WorldHeight()
    {
        Init();
        RefreshMap();
        return worldHeight / Mathf.Max(uvSize.x, uvSize.y, 0.01f);
    }

    public bool RayToUV(Ray ray, out Vector2 uv, out float dist)
    {
        uv = Vector2.zero;
        dist = 0f;
        Init();
        RefreshMap();
        if (!mapReady) return false;

        Vector3 o = transform.InverseTransformPoint(ray.origin);
        Vector3 d = transform.InverseTransformVector(ray.direction);

        float denom = Vector3.Dot(d, planeN);
        if (Mathf.Abs(denom) < 1e-8f) return false;

        float t = Vector3.Dot(P0 - o, planeN) / denom;
        if (t < 0f) return false;

        Vector3 w = (o + d * t) - P0;
        float a = Vector3.Dot(e1, e1), b = Vector3.Dot(e1, e2), c = Vector3.Dot(e2, e2);
        float dw1 = Vector3.Dot(w, e1), dw2 = Vector3.Dot(w, e2);
        float den = a * c - b * b;
        if (Mathf.Abs(den) < 1e-14f) return false;

        float s = (c * dw1 - b * dw2) / den;
        float q = (a * dw2 - b * dw1) / den;
        uv = T0 + s * dT1 + q * dT2;

        dist = t * ray.direction.magnitude;
        return true;
    }
}