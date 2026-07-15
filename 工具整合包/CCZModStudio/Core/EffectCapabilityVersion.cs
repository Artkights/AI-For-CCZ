using System.Reflection;

namespace CCZModStudio.Core;

public static class EffectCapabilityVersion
{
    public const string SchemaVersion = "effect-authoring-7.0";
    public const string BuildChannel = "ccz65-open-authoring-v7";

    public static string CoreVersion => typeof(EffectCapabilityVersion).Assembly.GetName().Version?.ToString() ?? "未知";

    public static string BuildIdentity
    {
        get
        {
            var assembly = typeof(EffectCapabilityVersion).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "未知";
        }
    }
}
