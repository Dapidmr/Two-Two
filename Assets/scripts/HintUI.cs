using UnityEngine;
using UnityEngine.InputSystem;

// Tempat tunggal untuk semua tulisan petunjuk (Press E to Open, Press Esc to back, dst).
// Tidak perlu dipasang di objek mana pun: dibuat otomatis saat pertama kali dipanggil.
// Panggil tiap frame dari Update():
//   HintUI.ShowAim("Press E to Open");        // di bawah crosshair / kursor
//   HintUI.ShowBottom("Press Esc to back");   // tengah bawah layar
// Kalau beberapa script memanggil ShowAim di frame yang sama, hanya yang terakhir tampil,
// jadi tidak akan ada tulisan dobel.
public class HintUI : MonoBehaviour
{
    static HintUI inst;

    string aimText, bottomText;
    GUIStyle style, shadow;

    static HintUI Get()
    {
        if (inst == null)
        {
            var go = new GameObject("HintUI");
            inst = go.AddComponent<HintUI>();
        }
        return inst;
    }

    public static void ShowAim(string text) { Get().aimText = text; }
    public static void ShowBottom(string text) { Get().bottomText = text; }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        }

        if (!string.IsNullOrEmpty(aimText) && Mouse.current != null)
        {
            Vector2 p = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : new Vector2(Mouse.current.position.ReadValue().x, Screen.height - Mouse.current.position.ReadValue().y);
            Draw(aimText, new Rect(p.x - 150, p.y + 25, 300, 40));
        }

        if (!string.IsNullOrEmpty(bottomText))
            Draw(bottomText, new Rect(Screen.width * 0.5f - 250, Screen.height - 80, 500, 40));

        // hapus setelah digambar; script yang masih butuh akan memanggil lagi di frame berikutnya
        if (Event.current.type == EventType.Repaint)
        {
            aimText = null;
            bottomText = null;
        }
    }

    void Draw(string text, Rect r)
    {
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, shadow);
        GUI.Label(r, text, style);
    }
}