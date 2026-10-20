using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;          // drag Capsule ke sini di Inspector
    public Vector3 offset = new Vector3(0f, 5f, -7f); // jarak kamera dari target
    public float smoothSpeed = 5f;    // makin besar, makin cepat kamera ngejar

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        transform.LookAt(target);
    }
}