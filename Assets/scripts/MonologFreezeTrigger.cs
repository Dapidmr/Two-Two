using System.Collections;
using UnityEngine;

// Versi trigger untuk MonologFreeze (script baru). Kalau mau tetap pakai MonologTrigger lama
// punya temenmu untuk Monolog versi lama, biarkan saja berdampingan — ini khusus untuk
// momen yang butuh freeze.
public class MonologFreezeTrigger : MonoBehaviour
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
        if (MonologFreeze.Instance == null)
        {
            Debug.LogWarning("MonologFreezeTrigger " + name + ": tidak ada MonologFreeze di scene");
            return;
        }

        played = true;
        if (delay > 0f) StartCoroutine(PlayDelayed());
        else MonologFreeze.Instance.Play(lines);
    }

    IEnumerator PlayDelayed()
    {
        yield return new WaitForSeconds(delay);
        MonologFreeze.Instance.Play(lines);
    }

    void OnTriggerEnter(Collider other)
    {
        if (playOnEnter && other.CompareTag(triggerTag)) Play();
    }
}