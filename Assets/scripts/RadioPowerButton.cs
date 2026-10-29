using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class RadioPowerButton : MonoBehaviour
{
    public RadioTuner tuner;             // kosongkan kalau RadioTuner ada di induk objek ini
    public float pressScale = 0.9f;      // tombol mengecil sebentar saat ditekan

    private Vector3 startScale;
    private float pressTimer = 0f;
    private Collider col;

    void Start()
    {
        col = GetComponent<Collider>();
        startScale = transform.localScale;
        if (tuner == null) tuner = GetComponentInParent<RadioTuner>();
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;

        // Collider mati = pemain belum zoom, jadi tombol tidak bisa ditekan
        if (col.enabled && Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverThisButton())
        {
            if (tuner != null) tuner.PressPower();
            pressTimer = 0.12f;
        }

        pressTimer -= Time.deltaTime;
        float s = pressTimer > 0f ? pressScale : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale, startScale * s, Time.deltaTime * 20f);
    }

    bool IsMouseOverThisButton()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        RadioPowerButton closest = null;
        float closestDist = float.MaxValue;
        foreach (var h in hits)
        {
            var b = h.collider.GetComponent<RadioPowerButton>();
            if (b != null && h.distance < closestDist)
            {
                closest = b;
                closestDist = h.distance;
            }
        }
        return closest == this;
    }
}