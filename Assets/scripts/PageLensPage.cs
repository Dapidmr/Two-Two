using UnityEngine;

public class PageLensPage : MonoBehaviour
{
    public Material lensMaterial;
    public Texture gibberish;
    public Texture readable;
    public float aspect = 0.71f;
    public int monocleId = 1;   // monocle mana yang boleh membaca halaman ini (1 atau 2)

    private Material mat;
    private MeshCollider col;

    // Pemetaan posisi lokal -> UV, diambil dari UV asli mesh
    private bool mapReady = false;
    private Vector3 P0, e1, e2, planeN;
    private Vector2 T0, dT1, dT2;

    void Awake() { Init(); }

    void Init()
    {
        if (mat != null) return;

        mat = new Material(lensMaterial);
        foreach (var n in new[] { "_GibberishTex", "_ReadableTex", "_Aspect", "_LensUV", "_LensRadius", "_LensOn" })
            if (!mat.HasProperty(n)) Debug.LogError($"[{name}] shader tidak punya property {n}");

        if (gibberish == null || readable == null)
            Debug.LogError($"[{name}] texture gibberish/readable belum di-assign");

        mat.SetTexture("_GibberishTex", gibberish);
        mat.SetTexture("_ReadableTex", readable);
        mat.SetFloat("_Aspect", aspect);
        mat.SetFloat("_LensOn", 0f);
        GetComponent<Renderer>().material = mat;

        BuildMap();
    }

    void BuildMap()
    {
        mapReady = false;
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        Mesh m = mf.sharedMesh;
        Vector3[] v = m.vertices;
        Vector2[] uvs = m.uv;
        int[] tr = m.triangles;
        if (uvs.Length != v.Length) { Debug.LogError($"[{name}] mesh tidak punya UV"); return; }

        // Cari segitiga pertama yang tidak degenerate
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
        Debug.LogError($"[{name}] tidak menemukan segitiga valid untuk pemetaan UV");
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

    public float WorldHeight()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 1f;
        Vector3 s = Vector3.Scale(mf.sharedMesh.bounds.size, transform.lossyScale);
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    // Perpotongan ray dengan bidang halaman -> UV (boleh di luar 0..1)
    public bool RayToUV(Ray ray, out Vector2 uv, out float dist)
    {
        uv = Vector2.zero;
        dist = 0f;
        Init();
        if (!mapReady) return false;

        Vector3 o = transform.InverseTransformPoint(ray.origin);
        Vector3 d = transform.InverseTransformVector(ray.direction);   // memperhitungkan scale

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

        dist = t * ray.direction.magnitude;   // jarak dunia
        return true;
    }
}