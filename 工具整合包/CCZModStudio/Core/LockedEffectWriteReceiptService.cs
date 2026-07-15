using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 为所有特效写入提供同一套不可伪造、一次性、短期有效的进程内凭据。
/// </summary>
public sealed class LockedEffectWriteReceiptService
{
    public const string MetadataKey = "LockedEffectWriteReceipt";
    private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("N");
    private static readonly ConcurrentDictionary<string, RegisteredReceipt> Receipts =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public LockedEffectWriteReceipt Issue(
        CczProject project,
        EffectPackage package,
        string operationKind,
        TimeSpan? lifetime = null)
    {
        if (package.PatchSegments.Count == 0)
            throw new InvalidOperationException("没有可锁定的特效写入段。");
        package.Metadata.Remove(MetadataKey);
        var identity = new ProjectPatchIdentityService().Build(project);
        var receipt = new LockedEffectWriteReceipt
        {
            ReceiptId = Guid.NewGuid().ToString("N"),
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            OperationKind = operationKind,
            ProjectId = identity.ProjectId,
            GameRoot = identity.GameRoot,
            ExeSha256 = identity.CurrentSha256,
            PackageHash = ComputePackageHash(package),
            ProcessInstanceId = ProcessInstanceId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15))
        };
        Receipts[receipt.ReceiptId] = new RegisteredReceipt(receipt, false);
        package.Metadata[MetadataKey] = JsonSerializer.Serialize(receipt, JsonOptions);
        PruneExpired();
        return receipt;
    }

    public LockedEffectWriteReceipt ValidateAndConsume(
        CczProject project,
        EffectPackage package,
        string? expectedOperationKind = null,
        ProjectPatchIdentity? verifiedCurrentIdentity = null)
    {
        new EffectReleaseConsistencyService().EnsureWriteAllowed();
        if (!package.Metadata.TryGetValue(MetadataKey, out var json) || string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("特效写入包缺少当前进程签发的一次性预览凭据，请重新预览。");
        var supplied = JsonSerializer.Deserialize<LockedEffectWriteReceipt>(json, JsonOptions)
                       ?? throw new InvalidOperationException("特效写入凭据无法读取，请重新预览。");
        if (!Receipts.TryGetValue(supplied.ReceiptId, out var registered) || registered.Consumed)
            throw new InvalidOperationException("特效写入凭据不存在、已使用或因进程重启失效，请重新预览。");

        var trusted = registered.Receipt;
        if (!FixedEquals(trusted.Nonce, supplied.Nonce) ||
            !FixedEquals(trusted.PackageHash, supplied.PackageHash) ||
            !FixedEquals(trusted.ProcessInstanceId, supplied.ProcessInstanceId))
            throw new InvalidOperationException("特效写入凭据已被修改，禁止写入。");
        if (trusted.ExpiresAtUtc <= DateTime.UtcNow)
        {
            Receipts.TryRemove(trusted.ReceiptId, out _);
            throw new InvalidOperationException("特效写入凭据已过期，请重新预览。");
        }
        if (!string.IsNullOrWhiteSpace(expectedOperationKind) &&
            !trusted.OperationKind.Equals(expectedOperationKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"写入凭据属于“{trusted.OperationKind}”，不能用于“{expectedOperationKind}”。");

        var identity = verifiedCurrentIdentity ?? new ProjectPatchIdentityService().Build(project);
        if (!trusted.ProjectId.Equals(identity.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !ProjectPatchIdentityService.NormalizePath(trusted.GameRoot).Equals(identity.GameRoot, StringComparison.OrdinalIgnoreCase) ||
            !trusted.ExeSha256.Equals(identity.CurrentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("项目身份或 EXE 已在预览后变化，请重新预览。");
        if (!trusted.PackageHash.Equals(ComputePackageHash(package), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("特效写入包的元数据、绑定、写入段或旧字节锁已被修改，禁止写入。");

        if (!Receipts.TryUpdate(trusted.ReceiptId, registered with { Consumed = true }, registered))
            throw new InvalidOperationException("特效写入凭据已被其他操作使用，请重新预览。");
        Receipts.TryRemove(trusted.ReceiptId, out _);
        return trusted;
    }

    public static string ComputePackageHash(EffectPackage package)
    {
        var builder = new StringBuilder()
            .Append(package.SchemaVersion).Append('|').Append(package.PackageId).Append('|')
            .Append(package.Domain).Append('|').Append(package.EffectId).Append('|')
            .Append(package.Name).Append('|').Append(package.Description).Append('|')
            .Append(package.EffectValue).Append('|').Append(package.SourcePrompt).Append('|')
            .Append(package.BackupNote).Append('|')
            .Append("authorization:").Append(JsonSerializer.Serialize(package.WriteAuthorization, JsonOptions)).Append('|');
        foreach (var link in package.SourceLinks.OrderBy(item => item, StringComparer.Ordinal))
            builder.Append("source:").Append(link).Append('|');
        foreach (var binding in package.Bindings
                     .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.RowId).ThenBy(item => item.ItemId)
                     .ThenBy(item => item.PersonId).ThenBy(item => item.JobId))
        {
            builder.Append("binding:").Append(JsonSerializer.Serialize(binding, JsonOptions)).Append('|');
        }
        foreach (var metadata in package.Metadata
                     .Where(item => !item.Key.Equals(MetadataKey, StringComparison.OrdinalIgnoreCase) &&
                                    !item.Key.Equals("CompositePreviewReceipt", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            builder.Append("metadata:").Append(metadata.Key).Append(':').Append(metadata.Value).Append('|');
        foreach (var segment in package.PatchSegments
                     .OrderBy(item => item.TargetFile, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.AddressKind, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Address))
            builder.Append("segment:").Append(JsonSerializer.Serialize(segment, JsonOptions)).Append('|');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in Receipts.Where(item => item.Value.Consumed || item.Value.Receipt.ExpiresAtUtc <= now))
            Receipts.TryRemove(pair.Key, out _);
    }

    private sealed record RegisteredReceipt(LockedEffectWriteReceipt Receipt, bool Consumed);
}
