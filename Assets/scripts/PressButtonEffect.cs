using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// Efek tombol turun saat ditekan (bergeser, bukan mengecil). Bisa dipasang di tombol apa pun.
// Collider objek ini harus ada di Control Colliders milik ZoomView supaya hanya bisa ditekan saat zoom.
[RequireComponent(typeof(Collider))]
public class PressButtonEffect : MonoBehaviour
{
    [Header("Efek tekan")]
    public Vector3 pressDirection = Vector3.down;   // arah tombol turun (sumbu lokal objek induknya)
    public float pressFraction = 0.5f;              // seberapa dalam turunnya, sebagai bagian dari ukuran tombol
    public float pressSpeed = 25f;
    public float minDownTime = 0.1f;                // minimal lama tombol tetap turun (detik)
    public bool stayDownWhileHeld = true;           // true = tetap turun selama klik ditahan

    [Header("Event")]
    public UnityEvent onPressed = new UnityEvent();

    private Vector3 upLocalPos;
    private Vector3 downLocalPos;
    private Collider col;
    private bool holding = false;
    private float downTimer = 0f;

    void Start()
    {
        col = GetComponent<Collider>();

        // Posisi naik = posisi awal. Posisi turun dihitung sekali di sini.
        upLocalPos = transform.localPosition;

        Vector3 dirWorld = DirWorld(transform, pressDirection);
        Vector3 downWorld = transform.position + dirWorld * PressDistance(transform, dirWorld, pressFraction);
        downLocalPos = transform.parent != null
            ? transform.parent.InverseTransformPoint(downWorld)
            : downWorld;
    }

    void Update()
    {
        if (Camera.main != null && Mouse.current != null)
        {
            // Klik pada tombol (hanya saat collider menyala, yaitu saat zoom)
            if (col.enabled && Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverThis())
            {
                holding = true;
                downTimer = minDownTime;
                onPressed.Invoke();
            }

            if (holding && !Mouse.current.leftButton.isPressed)
                holding = false;
        }

        downTimer -= Time.deltaTime;
        bool pressed = (stayDownWhileHeld && holding) || downTimer > 0f;

        Vector3 target = pressed ? downLocalPos : upLocalPos;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * pressSpeed);
    }

    bool IsMouseOverThis()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        PressButtonEffect closest = null;
        float closestDist = float.MaxValue;
        foreach (var h in hits)
        {
            var b = h.collider.GetComponent<PressButtonEffect>();
            if (b != null && h.distance < closestDist)
            {
                closest = b;
                closestDist = h.distance;
            }
        }
        return closest == this;
    }

    // ---- Fungsi bantu (dipakai juga oleh RadioKeypad) ----

    public static Vector3 DirWorld(Transform t, Vector3 dirLocal)
    {
        Vector3 d = dirLocal.sqrMagnitude > 0f ? dirLocal.normalized : Vector3.down;
        return t.parent != null ? t.parent.TransformDirection(d) : d;
    }

    public static float PressDistance(Transform t, Vector3 dirWorld, float fraction)
    {
        Renderer r = t.GetComponentInChildren<Renderer>();
        if (r == null) return fraction * 0.01f;

        Vector3 a = new Vector3(Mathf.Abs(dirWorld.x), Mathf.Abs(dirWorld.y), Mathf.Abs(dirWorld.z));
        return Vector3.Dot(r.bounds.size, a) * fraction;
    }

    // Garis kuning di Scene view: ke mana tombol akan turun (muncul saat objek dipilih)
    void OnDrawGizmosSelected()
    {
        Vector3 d = DirWorld(transform, pressDirection);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + d * PressDistance(transform, d, pressFraction));
    }
}