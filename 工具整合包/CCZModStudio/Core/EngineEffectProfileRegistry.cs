using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>Single versioned source of truth for effect-authoring EXE identities.</summary>
public static class EngineEffectProfileRegistry
{
    public const string Canonical65Sha256 = "F9A357585F4D77C273A8BADD3E4EF4062983CC81D8040C0CB74768FAB7C04F5F";
    public const string Known65VariantSha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3";
    public const string Profile65Id = "ccz65-unencrypted-effect-authoring-v3";

    private static readonly EngineEffectProfileDefinition Profile65 = new()
    {
        ProfileId = Profile65Id,
        ProfileVersion = 3,
        EngineVersion = "6.5",
        FileLength = 1_196_032,
        ImageBase = 0x00400000,
        CanonicalSha256 = Canonical65Sha256,
        // Normalization writes the canonical field value (0x91), so this currently
        // equals the canonical complete hash while remaining a separate identity.
        NormalizedIdentitySha256 = Canonical65Sha256,
        PeSectionLayout =
        [
            "00001000:00085000:00000400:00084E00:E0000020",
            "00086000:00005000:00085200:00004800:C0000040",
            "0008B000:00033000:00089A00:00006800:C0000040",
            "000BE000:00010000:00090200:0000F600:E0000060",
            "000CE000:00064000:0009F800:00064000:E0000060",
            "00132000:000206B0:00103800:00020800:40000040"
        ],
        KnownFullSha256 = [Canonical65Sha256, Known65VariantSha256],
        MaximumChangedBytes = 32,
        MaximumChangedRanges = 16,
        ForbiddenRangeKinds = ["PeHeaders", "ImportTable", "RelocationTable", "Hook", "ControlFlow", "CodeCave", "Allocation"],
        RegisteredFields =
        [
            new ExecutableRegisteredField
            {
                FieldId = "strategy-extension-flags",
                DisplayNameZh = "策略扩展标记",
                FileOffset = 0xA2CE0,
                VirtualAddress = 0x004D14E0,
                Width = 1,
                Encoding = "bit-flags-u8",
                CanonicalValueHex = "91",
                AllowedBitMask = 0xFF,
                AllowedValuesHex = ["90", "91"],
                Minimum = 0,
                Maximum = 0xFF,
                AffectsAbi = false,
                AffectsHook = false
            }
        ]
    };

    public static IReadOnlyList<EngineEffectProfileDefinition> All => [Profile65];
    public static EngineEffectProfileDefinition Current65 => Profile65;
    public static EngineEffectProfileDefinition? FindByFullSha(string sha256)
        => All.FirstOrDefault(profile => profile.KnownFullSha256.Contains(sha256, StringComparer.OrdinalIgnoreCase));
}
