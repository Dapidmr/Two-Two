using UnityEngine;
using UnityEngine.Events;

// Dasar untuk satu section di box Pandora (kode, section kanan atas, kompas).
// Bisa juga dipakai langsung sebagai placeholder section 2: centang "Solved" di Inspector.
public class PandoraPanel : MonoBehaviour
{
    [Header("Zoom ke section ini")]
    [Tooltip("Titik kamera. Buat objek kosong SEJAJAR (bukan anak) Pandora, posisikan saat Pandora belum diputar.")]
    public Transform focusPoint;
    public float focusFov = 0f;             // 0 = pakai FOV ZoomView

    [Header("Area klik di tampilan box")]
    [Tooltip("Collider tipis (non-trigger) di atas section ini. Aktif hanya saat zoom level 1.")]
    public Collider selector;

    [Header("Status")]
    public bool solved = false;
    public UnityEvent onSolved;

    [HideInInspector] public PandoraController owner;

    public virtual void Init(PandoraController controller)
    {
        owner = controller;
        if (selector != null) selector.enabled = false;
    }

    // Dipanggil saat kamera sudah diarahkan ke section ini
    public virtual void OnFocus() { }
    // Dipanggil saat kembali ke tampilan box
    public virtual void OnUnfocus() { }

    public void SetSolved()
    {
        if (solved) return;
        solved = true;
        onSolved.Invoke();
        if (owner != null) owner.NotifySolved(this);
    }
}