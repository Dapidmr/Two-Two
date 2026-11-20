using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

// Pasang di objek Pandora (yang punya ZoomView).
// Mengatur zoom level 2: klik section -> kamera zoom ke section itu.
public class PandoraController : MonoBehaviour
{
    public ZoomView zoomView;               // kosongkan kalau ada di objek yang sama
    public PandoraPanel[] panels;           // isi: section 1, (section 2 nanti), section 3

    [Header("UI")]
    public TextMeshProUGUI hintText;        // opsional
    public string overviewHint = "Klik bagian untuk zoom  |  Geser untuk memutar  |  Esc keluar";
    public string focusHint = "Esc kembali";

    [Header("Klik")]
    public float clickMaxPixels = 8f;       // geser lebih dari ini dianggap memutar, bukan klik

    [Header("Event")]
    public UnityEvent onAllSolved;

    private PandoraPanel current;
    private bool pressing;
    private Vector2 pressPos;
    private bool unlocked;

    void Awake()
    {
        if (zoomView == null) zoomView = GetComponent<ZoomView>();

        foreach (PandoraPanel p in panels)
            if (p != null) p.Init(this);

        zoomView.onEnter.AddListener(HandleEnter);
        zoomView.onExit.AddListener(HandleExit);
        zoomView.Unfocused += HandleUnfocused;

        SetHint(null);
    }

    void OnDestroy()
    {
        if (zoomView != null) zoomView.Unfocused -= HandleUnfocused;
    }

    void HandleEnter()
    {
        SetSelectors(true);
        SetHint(overviewHint);
    }

    void HandleExit()
    {
        SetSelectors(false);
        SetHint(null);
    }

    void HandleUnfocused()
    {
        if (current != null) current.OnUnfocus();
        current = null;
        SetSelectors(true);
        SetHint(overviewHint);
    }

    void Update()
    {
        if (Mouse.current == null || zoomView == null) return;

        if (!zoomView.IsZoomed || zoomView.IsFocused)
        {
            pressing = false;
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressing = true;
            pressPos = Mouse.current.position.ReadValue();
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && pressing)
        {
            pressing = false;
            float moved = Vector2.Distance(pressPos, Mouse.current.position.ReadValue());
            if (moved <= clickMaxPixels) TryPick();
        }
    }

    void TryPick()
    {
        Camera cam = Camera.main;
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        PandoraPanel best = null;
        float bestDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            foreach (PandoraPanel p in panels)
            {
                if (p == null || p.selector != h.collider) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = p;
                }
            }
        }

        if (best != null) Focus(best);
    }

    void Focus(PandoraPanel p)
    {
        if (p.focusPoint == null)
        {
            Debug.LogWarning("[Pandora] Focus Point belum diisi di " + p.name);
            return;
        }

        current = p;
        SetSelectors(false);
        zoomView.FocusOn(p.focusPoint, p.focusFov);
        p.OnFocus();
        SetHint(focusHint);
    }

    // Kembali ke zoom utama dari dalam section (dipanggil panel, misal saat kode benar)
    public void LeaveFocus()
    {
        if (zoomView != null) zoomView.Unfocus();
    }

    void SetSelectors(bool value)
    {
        foreach (PandoraPanel p in panels)
            if (p != null && p.selector != null)
                p.selector.enabled = value && !p.solved;    // section yang sudah solved tidak bisa di-zoom lagi
    }

    void SetHint(string text)
    {
        if (hintText == null) return;
        hintText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        if (!string.IsNullOrEmpty(text)) hintText.text = text;
    }

    // Dipakai kompas: benar/salahnya baru boleh diketahui kalau section lain sudah beres
    public bool OthersSolved(PandoraPanel except)
    {
        foreach (PandoraPanel p in panels)
            if (p != null && p != except && !p.solved) return false;
        return true;
    }

    public void NotifySolved(PandoraPanel p)
    {
        if (unlocked) return;
        foreach (PandoraPanel x in panels)
            if (x != null && !x.solved) return;

        unlocked = true;
        Debug.Log("[Pandora] Semua section selesai");
        onAllSolved.Invoke();
    }
}