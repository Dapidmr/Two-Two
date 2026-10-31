using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class Bookshelf : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI promptText;      // tulisan "Tekan E"
    public float heightOffset = 1.5f;

    [Header("Kamera")]
    public Transform zoomPoint;             // empty object di depan rak, tempat kamera berhenti
    public float cameraSpeed = 5f;

    [Header("Buku")]
    public Collider[] bookColliders;        // semua collider buku di rak ini

    [Header("Player")]
    public MonoBehaviour playerController;  // script gerak player, dimatikan saat zoom
    public Renderer[] playerRenderers;      // mesh player yang disembunyikan saat zoom

    [Header("Keluar dari zoom")]
    public bool allowExit = true;           // false = pemain tidak bisa keluar (diatur script lain, misal RadioTuner)
    
    [Header("Pan (drag mouse saat zoom)")]
    public Vector2 panLimit = new Vector2(0.5f, 0.3f); // batas geser (x = kiri-kanan, y = atas-bawah), satuan unit world
    public float dragSpeed = 0.0f;                   // unit world per pixel drag
    public float dragThreshold = 5f;                   // jarak minimal (pixel) sebelum dianggap drag, bukan klik

    [Header("Inspect buku")]
    public TextMeshProUGUI inspectHintText;  // isi teks "Press Esc to back", taruh di bawah layar
    public float dimMax = 0.75f;             // gelap maksimal (0-1)
    public float dimFadeSpeed = 4f;
    public float dimGap = 0.05f;             // jarak layar gelap di belakang buku

    public float inspectCameraBack = 0.4f;   // kamera mundur sejauh ini saat inspect

    public float panLimitX = 0.5f;       // batas geser kiri-kanan
    public float panLimitUp = 0.3f;      // batas kamera naik
    public float panLimitDown = 1.5f;    // batas kamera turun (lebih jauh)
    public float forwardAtBottom = 0.5f;  // seberapa jauh kamera maju saat sudah di batas bawah
    public bool IsDragging => isDragging;              // bisa dibaca script buku untuk membedakan klik vs drag


    private bool playerInRange = false;
    private bool isZoomed = false;
    private bool returning = false;

    private Transform cam;
    private Vector3 camOriginalPos;
    private Quaternion camOriginalRot;
    private Vector2 panOffset = Vector2.zero;
    private Vector2 pressStartPos;
    private bool isDragging = false;

    void Start()
    {
        cam = Camera.main.transform;

        // Simpan posisi awal kamera sejak Start
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        SetBooksInteractable(false);
        CreateDim();
        if (inspectHintText != null) inspectHintText.gameObject.SetActive(false);
        if (promptText != null) promptText.gameObject.SetActive(false);
        if (inspectHintText != null) Debug.Log("canvas = " + inspectHintText.canvas.name + " | mode = " + inspectHintText.canvas.renderMode + " | pos = " + inspectHintText.rectTransform.anchoredPosition);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (promptText != null && !isZoomed) promptText.gameObject.SetActive(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void Update()
    {
        // Prompt "Tekan E" mengikuti posisi rak
        if (playerInRange && !isZoomed && promptText != null)
        {
            Vector3 worldPos = transform.position + Vector3.up * heightOffset;
            promptText.transform.position = Camera.main.WorldToScreenPoint(worldPos);
        }

        bool ePressed = Keyboard.current.eKey.wasPressedThisFrame;
        bool escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (!isZoomed && playerInRange && ePressed)
            EnterZoom();
        else if (isZoomed && allowExit && !(inspecting || Time.frameCount == inspectEndFrame) && (ePressed || escPressed))
            ExitZoom();

        // Kamera hanya digerakkan saat zoom masuk atau keluar
        if (isZoomed)
        {
            // Drag dengan klik kiri ditahan
            if (!inspecting && Mouse.current.leftButton.wasPressedThisFrame)
            {
                pressStartPos = Mouse.current.position.ReadValue();
                isDragging = false;
            }

            if (!inspecting && Mouse.current.leftButton.isPressed)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                if (!isDragging && Vector2.Distance(mousePos, pressStartPos) > dragThreshold)
                    isDragging = true;

                if (isDragging)
                {
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    panOffset -= delta * dragSpeed;
                    panOffset.x = Mathf.Clamp(panOffset.x, -panLimitX, panLimitX);
                    panOffset.y = Mathf.Clamp(panOffset.y, -panLimitDown, panLimitUp);
                    Debug.Log($"panOffset.y = {panOffset.y} | down={panLimitDown} up={panLimitUp}");
                }
            }

            // 0 saat di tengah/atas, naik sampai 1 saat di batas bawah
            float downT = Mathf.Clamp01(-panOffset.y / panLimitDown);

            Vector3 targetPos = zoomPoint.position
                  + zoomPoint.right * panOffset.x
                  + zoomPoint.up * panOffset.y
                  + zoomPoint.forward * (downT * forwardAtBottom)
                  - zoomPoint.forward * (inspecting ? inspectCameraBack : 0f);

            cam.position = Vector3.Lerp(cam.position, targetPos, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, zoomPoint.rotation, Time.deltaTime * cameraSpeed);
        }
        else if (returning)
        {
            cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

            // Berhenti kalau sudah dekat
            if (Vector3.Distance(cam.position, camOriginalPos) < 0.01f)
            {
                cam.position = camOriginalPos;
                cam.rotation = camOriginalRot;
                returning = false;
            }
        }
    }

    void EnterZoom()
    {
        isZoomed = true;
        returning = false;
        panOffset = Vector2.zero;
        isDragging = false;

        // Simpan posisi kamera saat ini, supaya kembali ke posisi terakhir
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (promptText != null) promptText.gameObject.SetActive(false);
        if (playerController != null) playerController.enabled = false;
        SetPlayerVisible(false);

        SetBooksInteractable(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ExitZoom()
    {
        returning = true;
        isZoomed = false;

        SetBooksInteractable(false);
        if (playerController != null) playerController.enabled = true;
        SetPlayerVisible(true);
        if (playerInRange && promptText != null) promptText.gameObject.SetActive(true);

        // Kalau game kamu pakai cursor terkunci (first person), aktifkan ini:
        // Cursor.lockState = CursorLockMode.Locked;
        // Cursor.visible = false;
    }

    void SetBooksInteractable(bool value)
    {
        foreach (var c in bookColliders)
            if (c != null) c.enabled = value;
    }

    void SetPlayerVisible(bool value)
    {
        foreach (var r in playerRenderers)
            if (r != null) r.enabled = value;
    }
    // ===== Inspect support =====
    private bool inspecting = false;
    private int inspectEndFrame = -1;
    private Transform dimQuad;
    private Material dimMat;
    private float dimAlpha = 0f;
    private float dimTarget = 0f;

    public bool IsInspecting => inspecting;

    public void BeginInspect(float bookDistance, float bookRadius = 0f)
    {
        inspecting = true;
        float dist = bookDistance + bookRadius + dimGap;
        dimQuad.localPosition = new Vector3(0f, 0f, dist);

        // Skala quad supaya menutupi seluruh layar di jarak itu (+ margin 20%)
        Camera c = Camera.main;
        float h = 2f * dist * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * c.aspect;
        dimQuad.localScale = new Vector3(w * 1.2f, h * 1.2f, 1f);

        dimTarget = dimMax;
        if (inspectHintText != null) inspectHintText.gameObject.SetActive(true);
        Debug.Log("hint text = " + (inspectHintText == null ? "NULL" : inspectHintText.name));
    }

    public void EndInspect()
    {
        inspecting = false;
        inspectEndFrame = Time.frameCount;   // cegah Esc yang sama ikut keluar dari zoom
        dimTarget = 0f;
        if (inspectHintText != null) inspectHintText.gameObject.SetActive(false);
    }

    void CreateDim()
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "InspectDim";
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(cam, false);
        q.transform.localRotation = Quaternion.identity;
        q.transform.localScale = new Vector3(10f, 10f, 1f);

        var r = q.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        dimMat = new Material(Shader.Find("Sprites/Default"));
        dimMat.color = new Color(0f, 0f, 0f, 0f);
        r.material = dimMat;

        dimQuad = q.transform;
        q.SetActive(false);
    }

    

    void LateUpdate()
    {
        if (dimQuad == null) return;
        dimAlpha = Mathf.MoveTowards(dimAlpha, dimTarget, Time.deltaTime * dimFadeSpeed);
        dimMat.color = new Color(0f, 0f, 0f, dimAlpha);
        dimQuad.gameObject.SetActive(dimAlpha > 0.001f);
    }
}