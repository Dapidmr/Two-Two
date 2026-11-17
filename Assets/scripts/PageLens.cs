using UnityEngine;

public class PageLensPage : MonoBehaviour
{
    public Material lensMaterial;     // drag PageLensMat
    public Texture gibberish;
    public Texture readable;
    public float aspect = 0.75f;

    private Material mat;
    private MeshCollider col;

    void Start()
    {
        mat = new Material(lensMaterial);
        mat.SetTexture("_GibberishTex", gibberish);
        mat.SetTexture("_ReadableTex", readable);
        mat.SetFloat("_Aspect", aspect);
        GetComponent<Renderer>().sharedMaterial = mat;
    }

    public void SetCollider(bool on)
    {
        if (col == null && on) col = gameObject.AddComponent<MeshCollider>();
        if (col != null) col.enabled = on;
    }

    public void SetLens(Vector2 uv, float radius, bool on)
    {
        mat.SetVector("_LensUV", new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_LensRadius", radius);
        mat.SetFloat("_LensOn", on ? 1f : 0f);
    }
}