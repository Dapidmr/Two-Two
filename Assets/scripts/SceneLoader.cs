using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadCharacterA()
    {
        SceneManager.LoadScene("Assets/Scenes/CharacterA/Room-004.unity");
    }

    public void LoadCharacterB()
    {
        SceneManager.LoadScene("Assets/Scenes/CharacterB/Room-999.unity");
    }
}