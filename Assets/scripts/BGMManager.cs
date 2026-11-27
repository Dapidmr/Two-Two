using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tempel script ini ke GameObject kosong (misal namanya "BGMManager")
// yang ada di scene Home, lalu pasang AudioSource di GameObject yang sama
// (uncheck Play On Awake, drag musik ke slot AudioClip).
//
// stopOnScenes: isi dengan NAMA FILE scene-nya saja (tanpa folder, tanpa .unity),
// misalnya "Room-004" dan "Room-999", bukan path lengkapnya.
public class BGMManager : MonoBehaviour
{
    public static BGMManager Instance;

    [Tooltip("Nama file scene (tanpa folder & tanpa .unity) yang membuat BGM ini berhenti")]
    public string[] stopOnScenes;

    [Tooltip("Lama waktu fade-out saat BGM berhenti, biar nyambung sama transisi SceneFader")]
    public float stopFadeDuration = 1f;

    private AudioSource audioSource;
    private float baseVolume;
    private bool isStopping;

    void Awake()
    {
        // Supaya BGM gak dobel kalau scene Home ke-load ulang
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
        baseVolume = audioSource.volume > 0f ? audioSource.volume : 1f;
        audioSource.volume = baseVolume;

        if (!audioSource.isPlaying)
            audioSource.Play();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (isStopping) return;

        foreach (string targetScene in stopOnScenes)
        {
            if (scene.name == targetScene)
            {
                isStopping = true;
                StartCoroutine(FadeOutAndDestroy());
                return;
            }
        }
    }

    IEnumerator FadeOutAndDestroy()
    {
        float startVolume = audioSource.volume;
        float t = 0f;

        while (t < stopFadeDuration)
        {
            t += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, t / stopFadeDuration);
            yield return null;
        }

        audioSource.Stop();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}