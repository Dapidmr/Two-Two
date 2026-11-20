using System;

public static class GameState
{
    public static bool hasMonocle { get; private set; }
    public static bool hasMonocle2 { get; private set; }
    public static event Action onChanged;

    public static void SetMonocle(bool value)
    {
        hasMonocle = value;
        if (onChanged != null) onChanged.Invoke();
    }

    public static void SetMonocle2(bool value)
    {
        hasMonocle2 = value;
        if (onChanged != null) onChanged.Invoke();
    }
}