using UnityEngine;
using UnityEngine.UI;

// Lingkaran indikator di layar (dibuat lewat kode, tidak perlu Canvas atau gambar manual).
// Dipakai oleh PandoraCodePanel. Warna berubah halus dan berdenyut sebentar saat hasil keluar.
public class PandoraIndicator
{
    private GameObject root;
    private Image fill;
    private RectTransform rim;
    private Color current;
    private Color target;
    private float pulseT = 99f;
    private static Sprite circleSprite;

    public bool IsBuilt { get { return root != null; } }

    public void Build(string name, Vector2 screenPos, float size, Color waitColor, Color rimColor)
    {
        if (root != null) return;

        root = new GameObject(name);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Bingkai gelap
        GameObject rimGo = new GameObject("Rim", typeof(RectTransform));
        rimGo.transform.SetParent(root.transform, false);
        Image rimImg = rimGo.AddComponent<Image>();
        rimImg.sprite = GetCircle();
        rimImg.color = rimColor;
        rimImg.raycastTarget = false;

        rim = rimImg.rectTransform;
        rim.anchorMin = screenPos;
        rim.anchorMax = screenPos;
        rim.pivot = new Vector2(0.5f, 0.5f);
        rim.sizeDelta = new Vector2(size, size);
        rim.anchoredPosition = Vector2.zero;

        // Isi berwarna
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(rimGo.transform, false);
        fill = fillGo.AddComponent<Image>();
        fill.sprite = GetCircle();
        fill.color = waitColor;
        fill.raycastTarget = false;

        RectTransform frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0.5f, 0.5f);
        frt.anchorMax = new Vector2(0.5f, 0.5f);
        frt.pivot = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(size * 0.78f, size * 0.78f);
        frt.anchoredPosition = Vector2.zero;

        current = target = waitColor;
    }

    public void Show(bool value)
    {
        if (root != null) root.SetActive(value);
    }

    // Ganti warna tujuan. snap = true: langsung berubah tanpa transisi.
    public void Set(Color c, bool pulse, bool snap = false)
    {
        target = c;
        if (snap) current = c;
        if (pulse) pulseT = 0f;
    }

    // Panggil tiap frame
    public void Tick()
    {
        if (fill == null) return;

        current = Color.Lerp(current, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        fill.color = current;

        if (pulseT < 0.5f)
        {
            pulseT += Time.deltaTime;
            float s = 1f + 0.25f * Mathf.Sin(Mathf.Clamp01(pulseT / 0.5f) * Mathf.PI);
            rim.localScale = Vector3.one * s;
        }
        else
        {
            rim.localScale = Vector3.one;
        }
    }

    public void Destroy()
    {
        if (root != null) Object.Destroy(root);
        root = null;
    }

    // Lingkaran putih dibuat lewat kode (juga dipakai sebagai tanda di tombol)
    public static Sprite GetCircle()
    {
        if (circleSprite != null) return circleSprite;

        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float r = size * 0.5f;
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01(r - d);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        circleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }
}