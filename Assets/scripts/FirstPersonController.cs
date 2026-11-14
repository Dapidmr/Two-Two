using UnityEngine;
using UnityEngine.InputSystem;

// Pengganti script gerak lama untuk mode first person:
// WASD bergerak mengikuti arah pandang, mouse untuk melihat sekeliling.
// Pasang di Capsule (pemain). Capsule harus punya Rigidbody dan Tag "Player".
[RequireComponent(typeof(Rigidbody))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Kamera")]
    public Transform cameraTransform;        // kosongkan = Main Camera
    public bool attachCameraOnStart = true;  // true = kamera otomatis dijadikan anak pemain saat game mulai
    public float eyeLocalHeight = 0.6f;      // tinggi mata dari titik tengah Capsule (Capsule tinggi 2 = 0.6). Sesuaikan dengan skala ruanganmu.
    public float nearClipPlane = 0.05f;      // supaya benda kecil tetap terlihat saat didekati

    [Header("Gerak")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 6.5f;         // tahan Left Shift

    [Header("Melihat")]
    public float lookSensitivity = 0.12f;
    public float maxPitch = 85f;             // batas melihat ke atas dan bawah (derajat)
    public bool invertY = false;

    [Header("Badan")]
    public bool hideBodyFromCamera = true;   // badan Capsule tidak terlihat kamera (bayangan tetap ada)

    private Rigidbody rb;
    private float yaw;
    private float pitch;
    private Vector2 moveInput;
    private bool sprint;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform != null)
        {
            if (attachCameraOnStart)
            {
                cameraTransform.SetParent(transform);
                cameraTransform.localPosition = new Vector3(0f, eyeLocalHeight, 0f);
                cameraTransform.localRotation = Quaternion.identity;
            }

            Camera c = cameraTransform.GetComponent<Camera>();
            if (c != null) c.nearClipPlane = nearClipPlane;
        }

        yaw = transform.eulerAngles.y;
        pitch = 0f;

        if (hideBodyFromCamera)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }

    // Saat script menyala (game mulai, atau setelah selesai zoom): kursor terkunci di tengah layar
    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Saat script dimatikan (misal oleh ZoomView saat zoom): berhenti bergerak dan kursor bebas
    void OnDisable()
    {
        moveInput = Vector2.zero;
        if (rb != null) rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null) return;

        // Melihat
        Vector2 d = Mouse.current.delta.ReadValue() * lookSensitivity;
        yaw += d.x;
        pitch += invertY ? d.y : -d.y;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // Gerak
        var k = Keyboard.current;
        float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        float z = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        moveInput = new Vector2(x, z);
        sprint = k.leftShiftKey.isPressed;
    }

    void FixedUpdate()
    {
        Vector3 dir = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float speed = sprint ? sprintSpeed : walkSpeed;
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(dir.x * speed, v.y, dir.z * speed);
    }
}