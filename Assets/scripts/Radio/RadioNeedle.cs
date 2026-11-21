using UnityEngine;

// Jarum frekuensi radio: bergeser mengikuti knob frekuensi.
// Knob di kiri penuh = jarum di ujung kiri, knob di kanan penuh = jarum di ujung kanan.
// Pasang di objek jarum.
//
// PENTING: posisikan jarum di Scene view PERSIS di ujung KIRI slot (posisi frekuensi paling kecil).
// Posisi itu dianggap titik awal oleh script ini, lalu jarum bergeser ke kanan sejauh Travel Distance.
public class RadioNeedle : MonoBehaviour
{
    [Header("Sumber")]
    public RadioKnob frequencyKnob;             // isi dengan knob frekuensi (knob kanan)

    [Header("Gerak jarum")]
    public Vector3 moveAxis = Vector3.right;    // arah geser, dihitung dari sumbu lokal objek induknya
    public float travelDistance = 0.1f;         // panjang slot (satuan lokal induk). Isi negatif kalau arahnya terbalik
    public float smoothSpeed = 15f;             // makin besar, makin cepat mengikuti knob (0 = langsung tanpa halus)

    private Vector3 startLocalPos;
    private Vector3 axisLocal;

    void Start()
    {
        startLocalPos = transform.localPosition;
        axisLocal = moveAxis.sqrMagnitude > 0f ? moveAxis.normalized : Vector3.right;

        if (frequencyKnob != null)
            transform.localPosition = TargetPos(frequencyKnob.value);
    }

    void Update()
    {
        if (frequencyKnob == null) return;

        Vector3 target = TargetPos(frequencyKnob.value);

        if (smoothSpeed <= 0f)
            transform.localPosition = target;
        else
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * smoothSpeed);
    }

    Vector3 TargetPos(float value01)
    {
        return startLocalPos + axisLocal * (travelDistance * Mathf.Clamp01(value01));
    }

    // Garis di Scene view: dari ujung kiri (cyan) ke ujung kanan (kuning), muncul saat jarum dipilih
    void OnDrawGizmosSelected()
    {
        Vector3 a = Vector3.right;
        if (moveAxis.sqrMagnitude > 0f) a = moveAxis.normalized;

        Vector3 startLocal = Application.isPlaying ? startLocalPos : transform.localPosition;
        Vector3 endLocal = startLocal + a * travelDistance;

        Vector3 startWorld = transform.parent != null ? transform.parent.TransformPoint(startLocal) : startLocal;
        Vector3 endWorld = transform.parent != null ? transform.parent.TransformPoint(endLocal) : endLocal;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(startWorld, 0.01f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(startWorld, endWorld);
        Gizmos.DrawWireSphere(endWorld, 0.01f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(startWorld, "MIN (taruh jarum di sini)");
        UnityEditor.Handles.Label(endWorld, "MAX");
#endif
    }
}