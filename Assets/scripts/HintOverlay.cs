using UnityEngine;
using UnityEngine.InputSystem;

// Panel hint di sisi kiri layar. Buka/tutup dengan tombol (default H) atau klik tab "Hint"
// (klik hanya berfungsi saat kursor bebas, misalnya saat zoom radio atau inspect buku).
// Pasang di GameObject mana saja (misalnya Player atau objek kosong "HintOverlay").
public class HintOverlay : MonoBehaviour
{
    [Header("Isi (kamu yang isi)")]
    public string title = "Hint";
    [TextArea(4, 12)] public string hintText = "";

    [Header("Kontrol")]
    public Key toggleKey = Key.H;
    public bool startOpen = false;

    [Header("Tampilan")]
    public float panelWidth = 320f;
    public float padding = 16f;
    public float slideSpeed = 10f;
    public int titleFontSize = 22;
    public int bodyFontSize = 18;
    public Color panelColor = new Color(0f, 0f, 0f, 0.78f);
    public Color textColor = Color.white;
    public float tabWidth = 64f;
    public float tabHeight = 56f;

    // true bila kursor sedang di atas panel/tab; dipakai script lain untuk mengabaikan klik
    public static bool PointerOver { get; private set; }

    bool open;
    float slide;          // 0 = tertutup, 1 = terbuka
    GUIStyle titleStyle, bodyStyle, tabStyle;

    void Start()
    {
        open = startOpen;
        slide = open ? 1f : 0f;
    }

    void OnDisable() { PointerOver = false; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) Toggle();
        slide = Mathf.MoveTowards(slide, open ? 1f : 0f, Time.unscaledDeltaTime * slideSpeed);
    }

    public void Toggle() { open = !open; }
    public void Open() { open = true; }
    public void Close() { open = false; }
    public void SetHint(string newText) { hintText = newText; }

    void OnGUI()
    {
        if (string.IsNullOrWhiteSpace(hintText)) { PointerOver = false; return; }

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            tabStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 18 };
        }
        titleStyle.fontSize = titleFontSize;
        bodyStyle.fontSize = bodyFontSize;
        titleStyle.normal.textColor = textColor;
        bodyStyle.normal.textColor = textColor;

        GUI.depth = -600;   // di atas Monolog (-500), di bawah layar hitam ending (-800)

        float innerW = panelWidth - padding * 2f;
        float titleH = titleStyle.CalcHeight(new GUIContent(title), innerW);
        float bodyH = bodyStyle.CalcHeight(new GUIContent(hintText), innerW);
        float panelH = padding * 2f + titleH + 6f + bodyH;

        float s = Mathf.SmoothStep(0f, 1f, slide);
        float x = Mathf.Lerp(-panelWidth, 0f, s);
        float y = (Screen.height - panelH) * 0.5f;
        var panel = new Rect(x, y, panelWidth, panelH);
        var tab = new Rect(x + panelWidth, y, tabWidth, tabHeight);

        if (s > 0.001f)
        {
            GUI.color = panelColor;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + padding, y + padding, innerW, titleH), title, titleStyle);
            GUI.Label(new Rect(x + padding, y + padding + titleH + 6f, innerW, bodyH), hintText, bodyStyle);
        }

        if (GUI.Button(tab, open ? "<" : toggleKey.ToString(), tabStyle)) Toggle();

        Vector2 m = Event.current.mousePosition;
        PointerOver = (s > 0.001f && panel.Contains(m)) || tab.Contains(m);
    }
}