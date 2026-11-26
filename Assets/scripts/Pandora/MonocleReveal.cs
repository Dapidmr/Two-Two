using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Menyembunyikan simbol (merah) pada sebuah objek sampai pemain punya monocle.
// Model dan texture asli TIDAK diubah: yang diganti hanya texture di salinan material saat game berjalan.
//  - Kalau "hidden texture" diberikan -> dipakai langsung sebagai tampilan tanpa simbol.
//  - Kalau tidak -> simbol merah terang dihapus otomatis dari salinan texture saat game mulai
//    (piksel merah tua seperti titik domino dibiarkan).
// Dipasang otomatis oleh PandoraBlockPanel, tidak perlu ditempel manual.
public class MonocleReveal : MonoBehaviour
{
    public enum Requirement { Monocle1, Monocle2, Either }

    private class Slot
    {
        public Material mat;
        public Texture original;
        public Texture hidden;
    }

    private readonly List<Slot> slots = new List<Slot>();
    private Requirement requirement = Requirement.Either;
    private bool forceReveal = false;
    private bool revealedNow = false;
    private bool applied = false;

    private static readonly Dictionary<Texture, Texture2D> cache = new Dictionary<Texture, Texture2D>();

    private static FieldInfo monocle2Field;
    private static PropertyInfo monocle2Prop;
    private static bool monocle2Searched = false;

    public void Setup(Requirement req, Texture hiddenOverride, float minRed, float dominance, int grow, Rect region, bool revealForTest)
    {
        requirement = req;
        forceReveal = revealForTest;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            // r.materials = salinan material, aset asli tidak berubah
            foreach (Material m in r.materials)
            {
                Texture tex = m.mainTexture;
                if (tex == null) continue;

                Texture hid = hiddenOverride != null ? hiddenOverride : GetAutoHidden(tex, minRed, dominance, grow, region);
                if (hid == null) continue;

                Slot s = new Slot();
                s.mat = m;
                s.original = tex;
                s.hidden = hid;
                slots.Add(s);
            }
        }

        // Langsung terapkan, supaya simbol tidak sempat terlihat di frame pertama
        Apply(forceReveal || HasMonocle(requirement));
    }

    void Update()
    {
        bool has = forceReveal || HasMonocle(requirement);
        if (applied && has == revealedNow) return;
        Apply(has);
    }

    void Apply(bool showSymbols)
    {
        foreach (Slot s in slots)
            if (s.mat != null) s.mat.mainTexture = showSymbols ? s.original : s.hidden;

        revealedNow = showSymbols;
        applied = true;
    }

    // ---------- Status monocle ----------

    static bool HasMonocle(Requirement req)
    {
        switch (req)
        {
            case Requirement.Monocle1: return GameState.hasMonocle;
            case Requirement.Monocle2: return HasMonocle2();
            default: return GameState.hasMonocle || HasMonocle2();
        }
    }

    // Nama variabel monocle 2 di GameState tidak kuketahui pasti, jadi dicari lewat reflection.
    static bool HasMonocle2()
    {
        if (!monocle2Searched)
        {
            monocle2Searched = true;
            System.Type t = typeof(GameState);
            BindingFlags fl = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;

            foreach (string n in new[] { "hasMonocle2", "hasMonocle_2", "monocle2", "hasSecondMonocle" })
            {
                monocle2Field = t.GetField(n, fl);
                if (monocle2Field != null) break;
                monocle2Prop = t.GetProperty(n, fl);
                if (monocle2Prop != null) break;
            }

            if (monocle2Field == null && monocle2Prop == null)
                Debug.LogWarning("[MonocleReveal] Variabel monocle 2 di GameState tidak ditemukan (dicari: hasMonocle2, hasMonocle_2, monocle2). Pakai 'Monocle1' atau beri tahu nama variabelnya.");
        }

        object v = null;
        if (monocle2Field != null) v = monocle2Field.GetValue(null);
        else if (monocle2Prop != null) v = monocle2Prop.GetValue(null, null);
        return v is bool && (bool)v;
    }

    // ---------- Hapus simbol otomatis ----------

    static Texture2D GetAutoHidden(Texture src, float minRed, float dominance, int grow, Rect region)
    {
        Texture2D cached;
        if (cache.TryGetValue(src, out cached)) return cached;

        Texture2D result = null;
        try
        {
            result = Erase(src, minRed, dominance, grow, region);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[MonocleReveal] Gagal membuat texture tanpa simbol untuk '" + src.name + "': " + ex.Message);
        }

        cache[src] = result;
        return result;
    }

    static Texture2D Erase(Texture src, float minRed, float dominance, int grow, Rect region)
    {
        int w = src.width;
        int h = src.height;

        // Salin texture lewat RenderTexture, jadi texture tidak perlu Read/Write di pengaturan import
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false);
        tex.wrapMode = src.wrapMode;
        tex.filterMode = src.filterMode;

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] px = tex.GetPixels32();
        int x0 = Mathf.Clamp(Mathf.RoundToInt(region.xMin * w), 0, w);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(region.xMax * w), 0, w);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(region.yMin * h), 0, h);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(region.yMax * h), 0, h);

        // 1. Tandai piksel merah terang (simbol). Merah tua (titik domino) tidak ikut.
        bool[] mask = new bool[px.Length];
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = y * w + x;
                float r = px[i].r / 255f;
                float g = px[i].g / 255f;
                float b = px[i].b / 255f;
                if (r >= minRed && r - g >= dominance && r - b >= dominance) mask[i] = true;
            }
        }

        // 2. Lebarkan tanda beberapa piksel supaya tepi pudar simbol ikut hilang
        for (int it = 0; it < grow; it++)
        {
            bool[] next = (bool[])mask.Clone();
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = y * w + x;
                    if (!mask[i]) continue;
                    if (x > 0) next[i - 1] = true;
                    if (x < w - 1) next[i + 1] = true;
                    if (y > 0) next[i - w] = true;
                    if (y < h - 1) next[i + w] = true;
                }
            }
            mask = next;
        }

        // 3. Warna kertas = rata-rata piksel terang yang bukan simbol
        float sr = 0f, sg = 0f, sb = 0f;
        int n = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = y * w + x;
                if (mask[i]) continue;
                float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                if (Mathf.Min(r, Mathf.Min(g, b)) < 0.6f) continue;
                sr += r; sg += g; sb += b;
                n++;
            }
        }
        Color32 paper = n > 0
            ? new Color32((byte)(sr / n * 255f), (byte)(sg / n * 255f), (byte)(sb / n * 255f), 255)
            : new Color32(245, 245, 245, 255);

        // 4. Isi bagian simbol dengan warna kertas
        for (int i = 0; i < px.Length; i++)
            if (mask[i]) px[i] = paper;

        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}