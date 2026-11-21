using UnityEngine;
using UnityEngine.InputSystem;

// Titik putih kecil sebagai pengganti kursor.
// - Kursor terkunci (first person): titik statis di tengah layar (aim).
// - Kursor bebas (zoom Bookshelf / MejaLaci / inspect buku): titik mengikuti mouse.
// Kursor sistem selalu disembunyikan. Pasang di GameObject yang selalu aktif (mis. Capsule player).
[DefaultExecutionOrder(2000)]
public class CrosshairDot : MonoBehaviour
{
    public float dotSize = 6f;                    // diameter titik (pixel)
    public Color dotColor = Color.white;
    public bool outline = true;                   // lingkaran gelap tipis di belakang, supaya terlihat di latar terang
    public float outlineSize = 1.5f;
    public bool confineToWindow = true;           // saat zoom, mouse tidak bisa keluar dari jendela game

    Texture2D dot;

    void Awake()
    {
        dot = MakeCircle(16);
    }

    void LateUpdate()
    {
        // LateUpdate: berjalan setelah script lain yang menyalakan kursor (Bookshelf, MejaLaci, FirstPersonController)
        Cursor.visible = false;

        if (confineToWindow && Cursor.lockState == CursorLockMode.None)
            Cursor.lockState = CursorLockMode.Confined;
    }

    void OnDisable()
    {
        Cursor.visible = true;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        GUI.depth = -1000;   // di atas semua GUI lain

        Vector2 p;
        if (Cursor.lockState == CursorLockMode.Locked || Mouse.current == null)
        {
            p = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
        else
        {
            Vector2 m = Mouse.current.position.ReadValue();
            p = new Vector2(m.x, Screen.height - m.y);   // Input System: y dari bawah, GUI: y dari atas
        }

        if (outline)
        {
            float s = dotSize + outlineSize * 2f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), dot);
        }

        GUI.color = dotColor;
        GUI.DrawTexture(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), dot);
        GUI.color = Color.white;
    }

    static Texture2D MakeCircle(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
        t.filterMode = FilterMode.Bilinear;
        float r = size * 0.5f;
        var center = new Vector2(r, r);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float a = Mathf.Clamp01(r - d);   // tepi halus 1 texel
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        t.Apply();
        return t;
    }
}