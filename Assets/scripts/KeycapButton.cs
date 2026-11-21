using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// Tombol keycap: klik -> tombol MASUK searah arah tekan yang diberikan (bukan turun ke bawah).
// Mode latch : tombol tetap masuk sampai Release() dipanggil.
// Mode biasa : tombol masuk selama klik ditahan (minimal minDownTime), lalu naik lagi.
// Ditambahkan otomatis oleh PandoraCodePanel dan RadioKeypad. Klik hanya terdeteksi saat Collider aktif.
public class KeycapButton : MonoBehaviour
{
    public UnityEvent onPressed = new UnityEvent();
    public bool interactable = true;
    public float minDownTime = 0.1f;
    public bool IsPressed { get; private set; }

    private Collider col;
    private ZoomView owner;          // untuk memeriksa apakah tombol tertutup badan benda (sisi belakang)
    private bool latch;
    private bool allowRepress;       // true = tombol yang sudah masuk boleh ditekan lagi (kode radio punya angka kembar)
    private float speed = 25f;
    private float releaseAt;
    private Vector3 startLocal;
    private Vector3 pressedLocal;
    private bool ready = false;

    // worldDirection = arah tombol bergerak saat ditekan (masuk ke dalam benda)
    public void Setup(Collider collider, Vector3 worldDirection, float pressFraction, float pressSpeed,
                      bool latchMode, bool repressWhenDown)
    {
        col = collider;
        owner = GetComponentInParent<ZoomView>();
        latch = latchMode;
        allowRepress = repressWhenDown;
        speed = pressSpeed;
        startLocal = transform.localPosition;

        Vector3 d = worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : -transform.up;

        Renderer r = GetComponentInChildren<Renderer>();
        Vector3 s = r != null ? r.bounds.size : Vector3.one * 0.02f;
        float dist = (Mathf.Abs(d.x) * s.x + Mathf.Abs(d.y) * s.y + Mathf.Abs(d.z) * s.z) * pressFraction;

        Vector3 offsetWorld = d * dist;
        Vector3 offsetLocal = transform.parent != null
            ? transform.parent.InverseTransformVector(offsetWorld)
            : offsetWorld;

        pressedLocal = startLocal + offsetLocal;
        ready = true;
    }

    // Naikkan tombol lagi
    public void Release()
    {
        IsPressed = false;
    }

    void Update()
    {
        if (!ready || Mouse.current == null) return;

        if (interactable && col != null && col.enabled &&
            Mouse.current.leftButton.wasPressedThisFrame &&
            (!IsPressed || allowRepress) && HitThis())
        {
            IsPressed = true;
            releaseAt = Time.time + minDownTime;
            onPressed.Invoke();
        }

        // Mode biasa: naik lagi setelah klik dilepas
        if (!latch && IsPressed && Time.time >= releaseAt && !Mouse.current.leftButton.isPressed)
            IsPressed = false;

        Vector3 target = IsPressed ? pressedLocal : startLocal;
        if ((transform.localPosition - target).sqrMagnitude > 0.00000001f)
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * speed);
        else
            transform.localPosition = target;
    }

    bool HitThis()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        KeycapButton closest = null;
        float closestDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            KeycapButton b = h.collider.GetComponent<KeycapButton>();
            if (b != null && h.distance < closestDist)
            {
                closest = b;
                closestDist = h.distance;
            }
        }

        return closest == this
            && (owner == null || owner.IsControlVisible(col))
            && ClickOcclusion.IsFrontmost(col, hits);   // <-- TAMBAHAN: harus paling depan dari SEMUA collider
    }
}