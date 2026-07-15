using CCZModStudio.Formats;
using CCZModStudio.Models;
using Iced.Intel;

namespace CCZModStudio.Core;

public sealed class HookSafetyAnalyzer
{
    public HookSafetyAnalysisResult Analyze(
        CczProject project,
        AssemblyPatchDraft draft,
        uint relocatedAddress)
    {
        var result = new HookSafetyAnalysisResult { HookAddress = draft.HookAddress };
        var targetPath = project.ResolveGameFile(draft.TargetFile);
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath);
        var pe = executable.PeImage;
        var contract = new HookContractService().Find(project, draft.HookContractId, draft.HookAddress);
        result.Contract = contract;

        if (contract != null && contract.HookAddress != draft.HookAddress)
        {
            result.Warnings.Add("Hook 地址与 HookContract 不一致。");
            result.Summary = "Hook 安全分析失败。";
            return result;
        }

        if (contract != null && !contract.AllowPreview)
        {
            result.Warnings.Add("该 HookContract 仅供解释和人工复核，未开放一键预览写入。");
            result.Summary = "Hook 安全分析失败。";
            return result;
        }

        var minimum = Math.Max(5, Math.Max(draft.OverwriteLength, contract?.MinimumOverwriteLength ?? 0));
        var offset = executable.VirtualAddressToFileOffset(draft.HookAddress);
        var section = pe.Sections.FirstOrDefault(item =>
            draft.HookAddress >= pe.ImageBase + item.VirtualAddress &&
            draft.HookAddress < pe.ImageBase + item.VirtualAddress + Math.Max(item.VirtualSize, item.RawSize));
        if (section == null || !section.IsExecutable)
        {
            result.Warnings.Add("Hook 地址不在可执行节中。");
            result.Summary = "Hook 安全分析失败。";
            return result;
        }

        var available = checked((int)Math.Min(64, pe.Bytes.LongLength - offset));
        var instructions = new X86InstructionScanner().DecodeBlock(
            pe.Bytes.AsSpan(checked((int)offset), available).ToArray(),
            draft.HookAddress,
            section.Name);
        var selected = new List<X86InstructionInfo>();
        var length = 0;
        foreach (var instruction in instructions)
        {
            selected.Add(instruction);
            length += instruction.Length;
            if (length >= minimum) break;
        }

        if (length < minimum)
        {
            result.Warnings.Add("无法解码出足够的完整原指令。");
            result.Summary = "Hook 安全分析失败。";
            return result;
        }

        result.RequiredOverwriteLength = length;
        result.ReturnAddress = checked(draft.HookAddress + (uint)length);
        result.OriginalInstructions = selected;
        result.CurrentBytesHex = ToHex(pe.Bytes.AsSpan(checked((int)offset), length).ToArray());
        var expected = NormalizeHex(draft.ExpectedOldBytesHex);
        var current = NormalizeHex(result.CurrentBytesHex);
        if (string.IsNullOrWhiteSpace(expected) || !current.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add($"旧字节锁与完整指令边界不一致；应为 {result.CurrentBytesHex}。");
            result.Summary = "Hook 安全分析失败。";
            return result;
        }

        draft.OverwriteLength = length;
        draft.ExpectedOldBytesHex = result.CurrentBytesHex;
        draft.ReturnAddress = result.ReturnAddress;
        draft.ReturnAddressHex = $"0x{result.ReturnAddress:X8}";

        if (selected.Any(instruction => instruction.IsReturn || instruction.IsIndirectJump))
        {
            result.Warnings.Add("覆盖范围包含 ret 或间接跳转，不能自动搬迁。");
            result.Summary = "Hook 安全分析失败。";
            return result;
        }

        var policy = FirstNonEmpty(draft.OriginalInstructionPolicy, contract?.OriginalInstructionPolicy);
        var paddingOnly = selected.SelectMany(instruction => instruction.Bytes).All(value => value is 0x90 or 0xCC);
        if (!paddingOnly && contract == null)
        {
            result.Warnings.Add("非填充 Hook 必须绑定当前 EXE 的 HookContract。");
        }
        if (string.IsNullOrWhiteSpace(policy))
        {
            policy = paddingOnly ? OriginalInstructionPolicies.PaddingOnly : string.Empty;
        }

        if (policy == OriginalInstructionPolicies.PaddingOnly && !paddingOnly)
        {
            result.Warnings.Add("Hook 覆盖的不是纯 NOP/CC 填充，不能按 PaddingOnly 处理。");
        }
        else if (policy == OriginalInstructionPolicies.AutoRelocate)
        {
            if (contract == null)
            {
                // The common missing-contract diagnostic was added above.
            }
            else if (!TryRelocate(selected, relocatedAddress, out var relocated, out var error))
            {
                result.Warnings.Add("原指令搬迁失败：" + error);
            }
            else
            {
                result.RelocatedOriginalBytes = relocated;
            }
        }
        else if (policy == OriginalInstructionPolicies.ChainExistingJumpTarget)
        {
            if (contract == null || contract.ExistingJumpTarget == 0)
            {
                result.Warnings.Add("链式保留策略缺少登记的既有跳转目标。");
            }
            else if (selected.Count != 1 || !selected[0].IsDirectJump || selected[0].BranchTarget != contract.ExistingJumpTarget)
            {
                result.Warnings.Add($"Hook 当前指令不是指向 0x{contract.ExistingJumpTarget:X8} 的唯一 rel32 跳转。");
            }
        }
        else if (policy == OriginalInstructionPolicies.HookReplacesOriginal && contract == null)
        {
            result.Warnings.Add("替换原逻辑必须由 HookContract 明确授权。");
        }
        else if (string.IsNullOrWhiteSpace(policy))
        {
            result.Warnings.Add("未声明原指令处理策略。");
        }

        if (contract != null && !contract.ExeSha256.Equals(new EnginePatchProfileService().Build(project).ExeSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add("HookContract 的 EXE SHA 与当前项目不一致。");
        }

        result.IsSafe = result.Warnings.Count == 0;
        result.Summary = result.IsSafe
            ? $"Hook 安全检查通过：覆盖 {length} 字节、{selected.Count} 条完整指令。"
            : "Hook 安全检查失败：" + string.Join("；", result.Warnings);
        return result;
    }

    private static bool TryRelocate(
        IReadOnlyList<X86InstructionInfo> source,
        uint relocatedAddress,
        out byte[] bytes,
        out string error)
    {
        bytes = [];
        error = string.Empty;
        try
        {
            var raw = source.SelectMany(instruction => instruction.Bytes).ToArray();
            var decoder = Decoder.Create(32, raw, source[0].Address, DecoderOptions.None);
            var instructions = new List<Instruction>();
            while (decoder.IP < source[0].Address + (uint)raw.Length)
            {
                var instruction = decoder.Decode();
                if (instruction.Length == 0) break;
                instructions.Add(instruction);
            }

            var writer = new RelocatedCodeWriter();
            var block = new InstructionBlock(writer, instructions, relocatedAddress);
            if (!BlockEncoder.TryEncode(32, block, out var encoderError, out _))
            {
                error = encoderError ?? "Iced BlockEncoder 未提供详细错误。";
                return false;
            }
            bytes = writer.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string NormalizeHex(string value)
        => string.Concat((value ?? string.Empty).Where(Uri.IsHexDigit)).ToUpperInvariant();

    private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ');

    private sealed class RelocatedCodeWriter : CodeWriter
    {
        private readonly List<byte> _bytes = [];
        public override void WriteByte(byte value) => _bytes.Add(value);
        public byte[] ToArray() => _bytes.ToArray();
    }
}
