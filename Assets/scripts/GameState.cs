using System;

public static class GameState
{
    public static bool hasMonocle { get; private set; }
    public static event Action onChanged;

    public static void SetMonocle(bool value)
    {
        hasMonocle = value;
        if (onChanged != null) onChanged.Invoke();
    }
}