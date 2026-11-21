using UnityEngine;
using UnityEngine.InputSystem;

// Membuka/menutup buku dengan AnimationClip (cara temanmu: SampleAnimation per phase).
// Panah kanan = buka, panah kiri = tutup. Hanya aktif saat ZoomView sedang zoom.
public class BookClipPlayer : MonoBehaviour
{
    public AnimationClip[] clips;        // clip berawalan "Plane|" dari model test book
    public float openDuration = 1.2f;    // lama animasi buka
    public bool requireZoom = true;
    public bool closeWhenLeaveZoom = true;

    private ZoomView zoomView;
    private float phase = 0f;            // 0 = tertutup, 0.5 = terbuka penuh
    private float target = 0f;

    void Start()
    {
        if (clips == null || clips.Length == 0)
            Debug.LogWarning("[BookClipPlayer] Clips masih kosong. Drag semua clip 'Plane|...' ke sini.");
        Apply();
    }

    bool Zoomed()
    {
        if (!requireZoom) return true;
        if (zoomView == null) zoomView = GetComponent<ZoomView>();
        return zoomView != null && zoomView.IsZoomed;
    }

    void Update()
    {
        bool zoomed = Zoomed();

        if (zoomed && Keyboard.current != null)
        {
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame) target = 0.5f;
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame) target = 0f;
        }
        if (!zoomed && closeWhenLeaveZoom) target = 0f;

        if (!Mathf.Approximately(phase, target))
        {
            float step = Time.deltaTime / Mathf.Max(0.01f, openDuration) * 0.5f;
            phase = Mathf.MoveTowards(phase, target, step);
            Apply();
        }
    }

    void Apply()
    {
        if (clips == null) return;
        foreach (var c in clips)
            if (c != null) c.SampleAnimation(gameObject, phase * c.length);
    }
}