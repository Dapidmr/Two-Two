using UnityEngine;
using UnityEngine.InputSystem;

// Taruh di root buku (Diary Book - Good), satu level dengan ZoomView.
public class BookPageFlipper : MonoBehaviour
{
    [Header("Referensi")]
    public Transform spine;            // Empty object di punggung buku, HARUS anak dari root buku
    public Transform[] pages;          // urutkan dari lembar paling ATAS ke paling bawah

    [Header("Putaran")]
    public Vector3 flipAxis = Vector3.forward;  // sumbu engsel, dalam ruang lokal Spine
    public float flipAngle = 180f;              // kalau arah balik salah, ganti -180
    public float flipSpeed = 300f;              // derajat per detik

    [Header("Anti tembus")]
    public Vector3 stackUp = Vector3.up;        // arah "tegak lurus lembar" di ruang lokal Spine
    public float stackGap = 0.0005f;            // jarak tiap lembar yang sudah dibalik

    private Transform[] hinges;
    private float[] current;
    private float[] target;
    private int next = 0;               // lembar berikutnya yang akan dibalik
    private ZoomView zoomView;

    void Start()
    {
        int n = pages.Length;
        hinges = new Transform[n];
        current = new float[n];
        target = new float[n];

        for (int i = 0; i < n; i++)
        {
            if (pages[i] == null) continue;

            GameObject h = new GameObject("PageHinge_" + i);
            h.transform.SetParent(spine, false);   // posisi 0 = tepat di spine
            hinges[i] = h.transform;
            pages[i].SetParent(hinges[i], true);   // true = posisi dunia lembar tidak berubah
        }
    }

    void Update()
    {
        if (zoomView == null) zoomView = GetComponent<ZoomView>();   // ZoomView bisa ditambahkan belakangan
           bool active = zoomView != null && zoomView.IsZoomed;

        if (active && Keyboard.current != null)
        {
            var k = Keyboard.current;
            if (k.rightArrowKey.wasPressedThisFrame) Flip(+1);
            if (k.leftArrowKey.wasPressedThisFrame) Flip(-1);
        }

        Animate();
    }

    void Flip(int dir)
    {
        if (dir > 0 && next < pages.Length)
        {
            target[next] = flipAngle;
            next++;
        }
        else if (dir < 0 && next > 0)
        {
            next--;
            target[next] = 0f;
        }
    }

    void Animate()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            if (hinges[i] == null) continue;

            current[i] = Mathf.MoveTowards(current[i], target[i], flipSpeed * Time.deltaTime);
            hinges[i].localRotation = Quaternion.AngleAxis(current[i], flipAxis.normalized);

            // Lembar yang sudah dibalik ditumpuk sedikit lebih tinggi supaya tidak z-fighting
            float t = Mathf.Abs(flipAngle) > 0.01f ? current[i] / flipAngle : 0f;
            hinges[i].localPosition = stackUp.normalized * (stackGap * i * t);
        }
    }
}