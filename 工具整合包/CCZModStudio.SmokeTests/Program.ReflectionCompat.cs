using CCZModStudio;

internal partial class Program
{
    private static readonly MainForm smokeForm = new();

    private static readonly System.Reflection.MethodInfo resolveEffectiveEffectId =
        typeof(MainForm).GetMethod("ResolveEffectiveItemEffectId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.ResolveEffectiveItemEffectId");

    private static readonly System.Reflection.MethodInfo buildEffectiveEffectIdText =
        typeof(MainForm).GetMethod("BuildItemEffectiveEffectIdText", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.BuildItemEffectiveEffectIdText");

    private static readonly System.Reflection.MethodInfo buildEffectiveEffectDescription =
        typeof(MainForm).GetMethod("BuildItemEffectiveEffectDescription", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.BuildItemEffectiveEffectDescription");
}
