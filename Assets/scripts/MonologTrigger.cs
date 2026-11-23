using System.Collections;
using UnityEngine;

// Memutar monolog lewat trigger zone (masuk area) atau dipanggil dari UnityEvent (Play).
public class MonologTrigger : MonoBehaviour
{
    [TextArea(2, 4)]
    public string[] lines;                   // satu elemen = satu baris monolog

    [Header("Pemicu otomatis (butuh Collider dengan Is Trigger)")]
    public bool playOnEnter = false;
    public string triggerTag = "Player";

    [Header("Aturan")]
    public bool playOnce = true;
    public float delay = 0f;

    bool played;

    // Dipanggil dari UnityEvent / script lain
    public void Play()
    {
        if (playOnce && played) return;
        if (Monolog.Instance == null)
        {
            Debug.LogWarning("MonologTrigger " + name + ": tidak ada Monolog di scene");
            return;
        }

        played = true;
        if (delay > 0f) StartCoroutine(PlayDelayed());
        else Monolog.Instance.Play(lines);
    }

    IEnumerator PlayDelayed()
    {
        yield return new WaitForSeconds(delay);
        Monolog.Instance.Play(lines);
    }

    void OnTriggerEnter(Collider other)
    {
        if (playOnEnter && other.CompareTag(triggerTag)) Play();
    }
}
