using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Menampilkan monolog player sebagai subtitle di bawah layar (efek ketik, baris demi baris).
// Pasang SATU di scene (misalnya di Capsule player). Dipanggil lewat Monolog.Instance.Play("baris 1", "baris 2")
// atau lewat MonologTrigger.
public class Monolog : MonoBehaviour
{
    public static Monolog Instance { get; private set; }

    [Header("Teks")]
    public float charsPerSecond = 35f;
    public float minHoldTime = 1.2f;        // lama teks bertahan setelah selesai diketik
    public float holdPerChar = 0.03f;       // tambahan lama per huruf
    public int fontSize = 26;
    public Color textColor = new Color(1f, 0.97f, 0.88f);
    public bool italic = true;

    [Header("Posisi")]
    public float bottomOffset = 160f;       // jarak dari bawah layar
    [Range(0.3f, 1f)] public float widthRatio = 0.7f;
    public bool dimBackground = true;

    [Header("Kartu (gaya perkamen)")]
    public bool useCard = true;                  // false = kembali ke latar gelap polos lama
    public string osFontName = "Georgia";        // font serif bawaan OS; kalau tidak ada, pakai font default
    public Color cardCenter = new Color(0.85f, 0.77f, 0.61f);
    public Color cardEdge = new Color(0.62f, 0.52f, 0.38f);
    public Color inkColor = new Color(0.08f, 0.06f, 0.05f);
    public float cardPadX = 56f;                 // jarak teks ke tepi kartu
    public float cardPadY = 38f;
    public float cornerRadius = 14f;

    Texture2D cardTex;
    Font cardFont;

    [Header("Kontrol")]
    public bool allowSkip = true;           // Space: selesaikan ketikan, tekan lagi untuk lanjut ke baris berikutnya

    string current = "";    // baris penuh (untuk menghitung tinggi supaya layout tidak loncat)
    string shown = "";      // bagian yang sudah diketik
    bool visible;
    bool skipRequested;
    Coroutine routine;
    GUIStyle style, shadow;

    public bool IsPlaying => routine != null;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("Ada lebih dari satu Monolog di scene. Hanya yang terakhir dipakai.");
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Play(params string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Run(lines));
    }

    public void Stop()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        visible = false;
        current = shown = "";
    }

    void Update()
    {
        if (allowSkip && visible && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            skipRequested = true;
    }

    IEnumerator Run(string[] lines)
    {
        visible = true;

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;

            current = line;
            shown = "";
            skipRequested = false;

            // ketik
            float t = 0f;
            while (shown.Length < line.Length)
            {
                t += Time.deltaTime * charsPerSecond;
                int n = Mathf.Min(line.Length, Mathf.FloorToInt(t));
                shown = line.Substring(0, n);
                if (skipRequested) { shown = line; skipRequested = false; }
                yield return null;
            }

            // tahan
            float hold = minHoldTime + line.Length * holdPerChar;
            float h = 0f;
            skipRequested = false;
            while (h < hold && !skipRequested)
            {
                h += Time.deltaTime;
                yield return null;
            }
        }

        visible = false;
        current = shown = "";
        routine = null;
    }

    void OnGUI()
    {
        if (!visible || string.IsNullOrEmpty(shown)) return;
        GUI.depth = -500;   // di bawah titik crosshair, di atas GUI lain

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            shadow = new GUIStyle(style);
        }
        if (useCard && cardTex == null) cardTex = MakeCardTexture();
        if (useCard && cardFont == null && !string.IsNullOrEmpty(osFontName))
            cardFont = Font.CreateDynamicFontFromOSFont(osFontName, fontSize);

        style.fontSize = shadow.fontSize = fontSize;
        style.font = shadow.font = useCard ? cardFont : null;
        style.fontStyle = shadow.fontStyle = useCard ? FontStyle.Bold : (italic ? FontStyle.Italic : FontStyle.Normal);
        style.normal.textColor = useCard ? inkColor : textColor;
        shadow.normal.textColor = Color.black;

        float w = Screen.width * widthRatio;
        float h = style.CalcHeight(new GUIContent(current), w);   // tinggi dari baris penuh
        var rect = new Rect((Screen.width - w) * 0.5f, Screen.height - bottomOffset - h, w, h);

        if (useCard)
        {
            float hintH = allowSkip ? 30f : 0f;   // ruang tambahan di dalam kartu untuk hint
            if (Event.current.type == EventType.Repaint)
                DrawCard(new Rect(rect.x - cardPadX, rect.y - cardPadY, rect.width + cardPadX * 2f, rect.height + cardPadY * 2f + hintH));
            GUI.Label(rect, shown, style);
            DrawNextHint(rect.yMax + 6f, true);
            return;
        }

        if (dimBackground && Event.current.type == EventType.Repaint)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(rect.x - 16f, rect.y - 8f, rect.width + 32f, rect.height + 16f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), shown, shadow);
        GUI.Label(rect, shown, style);
        DrawNextHint(rect.yMax + 18f, false);
    }
    Texture2D MakeCardTexture()
    {
        const int s = 64;
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f;
                float dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 1.2f);
                t.SetPixel(x, y, Color.Lerp(cardCenter, cardEdge, d * d));   // gelap di tepi
            }
        }
        t.Apply();
        return t;
    }

    static void Box(Rect r, float th, Color c)
    {
        var w = Texture2D.whiteTexture;
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, th), w);
        GUI.DrawTexture(new Rect(r.x, r.yMax - th, r.width, th), w);
        GUI.DrawTexture(new Rect(r.x, r.y, th, r.height), w);
        GUI.DrawTexture(new Rect(r.xMax - th, r.y, th, r.height), w);
        GUI.color = Color.white;
    }

    void DrawCard(Rect card)
    {
        // dasar perkamen dengan sudut membulat
        GUI.DrawTexture(card, cardTex, ScaleMode.StretchToFill, true, 0f, Color.white,
            Vector4.zero, new Vector4(cornerRadius, cornerRadius, cornerRadius, cornerRadius));

        Color ink = new Color(inkColor.r, inkColor.g, inkColor.b, 0.9f);
        Rect outer = new Rect(card.x + 12f, card.y + 12f, card.width - 24f, card.height - 24f);
        Rect inner = new Rect(card.x + 19f, card.y + 19f, card.width - 38f, card.height - 38f);
        Box(outer, 3f, ink);      // bingkai luar tebal
        Box(inner, 1.5f, ink);    // bingkai dalam tipis

        // kotak kecil di tiap sudut bingkai luar
        float s = 14f;
        Color fill = Color.Lerp(cardCenter, cardEdge, 0.5f);
        Vector2[] corners = {
        new Vector2(outer.xMin, outer.yMin), new Vector2(outer.xMax, outer.yMin),
        new Vector2(outer.xMin, outer.yMax), new Vector2(outer.xMax, outer.yMax)
    };
        foreach (var c in corners)
        {
            var r = new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s);
            GUI.color = fill;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            Box(r, 1.5f, ink);
        }
    }
    GUIStyle hintStyle, hintShadow;

    void DrawNextHint(float top, bool inCard)
    {
        if (!allowSkip) return;   // kalau skip dimatikan, hint tidak ditampilkan

        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            hintShadow = new GUIStyle(hintStyle);
            hintShadow.normal.textColor = Color.black;
        }
        hintStyle.fontSize = hintShadow.fontSize = inCard ? Mathf.Max(14, fontSize - 8) : 18;
        hintStyle.font = hintShadow.font = inCard ? cardFont : null;
        hintStyle.normal.textColor = inCard
            ? new Color(inkColor.r, inkColor.g, inkColor.b, 0.75f)   // tinta pudar di dalam kartu
            : Color.white;

        var r = new Rect((Screen.width - 240f) * 0.5f, top, 240f, 28f);
        if (!inCard) GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), "Space to next", hintShadow);
        GUI.Label(r, "Space to next", hintStyle);
    }
}
