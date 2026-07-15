using System.Security.Cryptography;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    private static void RunEffectOpenAuthoringV7Smoke(CczProject project)
    {
        var source = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        RunEffectSandboxPathSmoke(source);
        RunEffectAuthorizationBoundarySmoke(source);
        RunSignedDescendantTrustSmoke(source);
        RunEffectValidationPipeHandshakeSmoke();
        Console.WriteLine("EFFECT_OPEN_AUTHORING_V7_SMOKE_OK");
    }

    private static void RunEffectSandboxPathSmoke(CczProject source)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccz-effect-v7-sandbox-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(game);
        try
        {
            File.Copy(source.ResolveGameFile("Ekd5.exe"), Path.Combine(game, "Ekd5.exe"));
            var project = new CczProject { WorkspaceRoot = root, GameRoot = game, HexTableXmlPath = source.HexTableXmlPath };
            var sandbox = new EffectSandboxService().Create(project);
            var expectedRoot = Path.GetFullPath(Path.Combine(root, "CCZModStudio_TestCopies")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(sandbox.GameRoot).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
                !EffectSandboxService.TryRead(sandbox.GameRoot, out var descriptor) ||
                !Path.GetFullPath(descriptor.OriginalGameRoot).Equals(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("验证副本没有落在工作区测试目录，或签名沙箱标记无法验证。");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void RunEffectAuthorizationBoundarySmoke(CczProject source)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccz-effect-v7-auth-" + Guid.NewGuid().ToString("N"));
        var gameA = Path.Combine(root, "a");
        var gameB = Path.Combine(root, "b");
        Directory.CreateDirectory(gameA);
        Directory.CreateDirectory(gameB);
        try
        {
            File.Copy(source.ResolveGameFile("Ekd5.exe"), Path.Combine(gameA, "Ekd5.exe"));
            File.Copy(source.ResolveGameFile("Ekd5.exe"), Path.Combine(gameB, "Ekd5.exe"));
            var a = new CczProject { WorkspaceRoot = root, GameRoot = gameA, HexTableXmlPath = source.HexTableXmlPath };
            var b = new CczProject { WorkspaceRoot = root, GameRoot = gameB, HexTableXmlPath = source.HexTableXmlPath };
            var beforeA = Sha(a.ResolveGameFile("Ekd5.exe"));
            var beforeB = Sha(b.ResolveGameFile("Ekd5.exe"));

            var previewOnly = BuildNoChangeAuthorizationPackage(a, "preview-only");
            var preview = new EffectTransactionalPatchService().Preview(a, previewOnly);
            if (!preview.CanApply || previewOnly.WriteAuthorization == null || Sha(a.ResolveGameFile("Ekd5.exe")) != beforeA)
                throw new InvalidOperationException("结构化预览没有签发授权，或预览阶段修改了 EXE。");

            var peHeader = BuildNoChangeAuthorizationPackage(a, "pe-header-rejected");
            var headerBytes = File.ReadAllBytes(a.ResolveGameFile("Ekd5.exe")).AsSpan(6, 4).ToArray();
            peHeader.PatchSegments[0].Address = 6;
            peHeader.PatchSegments[0].BytesHex = Convert.ToHexString(headerBytes);
            peHeader.PatchSegments[0].ExpectedOldBytesHex = Convert.ToHexString(headerBytes);
            peHeader.PatchSegments[0].ModificationKind = EffectModificationKind.DirectEffectId;
            peHeader.PatchSegments[0].RequiredCapability = EffectWriteCapability.DirectParameter;
            peHeader.PatchSegments[0].SemanticFieldId = "native-direct-parameter:forged-header";
            peHeader.PatchSegments[0].SourceLocationId = "forged-header:0x6:4";
            var headerPreview = new EffectTransactionalPatchService().Preview(a, peHeader);
            if (headerPreview.CanApply || headerPreview.Warnings.All(item => !item.Contains("PE 文件头", StringComparison.Ordinal)))
                throw new InvalidOperationException("PE 文件头补丁没有在事务预览阶段被明确阻断。");

            previewOnly.WriteAuthorization.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            AssertApplyRejected(a, previewOnly, "过期", beforeA);

            var forged = BuildNoChangeAuthorizationPackage(a, "forged");
            RequirePreview(a, forged);
            forged.WriteAuthorization!.DecisionHash = new string('0', 64);
            AssertApplyRejected(a, forged, "摘要", beforeA);

            var crossProject = BuildNoChangeAuthorizationPackage(a, "cross-project");
            RequirePreview(a, crossProject);
            AssertApplyRejected(b, crossProject, "项目", beforeB);

            var sandboxProject = new EffectSandboxService().Create(a);
            var sandboxSha = Sha(sandboxProject.ResolveGameFile("Ekd5.exe"));
            var sandboxOnly = BuildNoChangeAuthorizationPackage(sandboxProject, "sandbox-only", EffectWriteRunMode.SandboxValidation);
            RequirePreview(sandboxProject, sandboxOnly);
            if (sandboxOnly.WriteAuthorization?.SandboxOnly != true)
                throw new InvalidOperationException("沙箱预览没有签发 SandboxOnly 授权。");
            File.Delete(Path.Combine(sandboxProject.GameRoot, EffectSandboxService.MarkerFileName));
            AssertApplyRejected(sandboxProject, sandboxOnly, "沙箱", sandboxSha);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void RunEffectValidationPipeHandshakeSmoke()
    {
        var release = new EffectReleaseConsistencyReport { IsConsistent = true };
        var client = EffectValidationHostClient.StartAsync(release).GetAwaiter().GetResult();
        try
        {
            var response = client.CallAsync<object, JsonElement>("ping", new { }).GetAwaiter().GetResult();
            if (!response.TryGetProperty("ready", out var ready) || !ready.GetBoolean() ||
                response.GetProperty("protocol").GetString() != EffectValidationPipeProtocol.Version)
                throw new InvalidOperationException("GameDebug 验证宿主握手结果不完整。");
            var pipeName = (string)(typeof(EffectValidationHostClient).GetField("_pipeName", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(client)
                ?? throw new InvalidOperationException("无法读取验证宿主管道名。"));
            var token = (string)(typeof(EffectValidationHostClient).GetField("_sessionToken", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(client)
                ?? throw new InvalidOperationException("无法读取验证宿主会话令牌。"));
            var abandoned = new EffectValidationPipeRequest
            {
                SessionToken = token,
                Action = "ping",
                Payload = JsonSerializer.SerializeToElement(new { })
            };
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(3000);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(abandoned, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            Thread.Sleep(100);
            var afterDisconnect = client.CallAsync<object, JsonElement>("ping", new { }).GetAwaiter().GetResult();
            if (!afterDisconnect.GetProperty("ready").GetBoolean())
                throw new InvalidOperationException("客户端中途断开后验证宿主没有继续服务。");
        }
        finally { client.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void RunSignedDescendantTrustSmoke(CczProject source)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccz-effect-v7-descendant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "Ekd5.exe");
            File.Copy(source.ResolveGameFile("Ekd5.exe"), exe);
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = source.HexTableXmlPath };
            var bytes = File.ReadAllBytes(exe);
            var beforeSha = Convert.ToHexString(SHA256.HashData(bytes));
            bytes[0x500] ^= 0x01;
            File.WriteAllBytes(exe, bytes);
            var afterSha = Convert.ToHexString(SHA256.HashData(bytes));
            ExecutableProfileAuditService.Invalidate([exe]);
            ExecutableAnalysisSnapshotCache.Shared.Invalidate([exe]);

            var manifest = new EffectManifest
            {
                ManifestId = "signed-descendant-smoke",
                ProjectRoot = root,
                ProjectIdentity = new ProjectPatchIdentityService().BuildKnown(project, "Ekd5.exe", bytes.LongLength, afterSha),
                Mode = "transactional-patch",
                Domain = "effect-v7-smoke",
                PackageId = "signed-descendant-smoke",
                Package = new EffectPackage { PackageId = "signed-descendant-smoke", Name = "签名后代测试" },
                Metadata =
                {
                    ["EngineProfileSha256Before"] = beforeSha,
                    ["EngineProfileSha256After"] = afterSha
                }
            };
            UserBoundSignatureService.Sign(manifest, static (item, value) => item.Signature = value);
            var manifestRoot = ProjectPatchIdentityService.EffectManifestRoot(project);
            Directory.CreateDirectory(manifestRoot);
            var manifestPath = Path.Combine(manifestRoot, "signed-descendant-smoke.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

            var trusted = new EffectWritableProfileService().Evaluate(project);
            if (!trusted.CanWrite || !trusted.IsTrackedDescendant ||
                trusted.ProfileAudit.TrustStatus != ExecutableProfileTrustStatus.TrackedDescendant)
                throw new InvalidOperationException("正确签名的事务谱系没有授予可追踪后代信任。");

            manifest.Package.Name = "签名后被篡改";
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            var tampered = new EffectWritableProfileService().Evaluate(project);
            if (tampered.CanWrite || tampered.IsTrackedDescendant)
                throw new InvalidOperationException("内容被篡改但未重新签名的 manifest 错误授予了后代信任。");

            manifest.Package.Name = "未签名历史记录";
            manifest.Signature = string.Empty;
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            var unsigned = new EffectWritableProfileService().Evaluate(project);
            if (unsigned.CanWrite || unsigned.IsTrackedDescendant)
                throw new InvalidOperationException("未签名 manifest 错误授予了后代信任。");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static EffectPackage BuildNoChangeAuthorizationPackage(CczProject project, string id, string runMode = EffectWriteRunMode.Formal)
    {
        const uint offset = 0xA2CE0;
        var bytes = File.ReadAllBytes(project.ResolveGameFile("Ekd5.exe"));
        var current = Convert.ToHexString(bytes.AsSpan(checked((int)offset), 1));
        return new EffectPackage
        {
            PackageId = id,
            Domain = "effect-v7-smoke",
            EffectId = 1,
            Metadata = { ["EffectWriteRunMode"] = runMode, ["LogicalPatchKind"] = "effect-v7-maintenance" },
            PatchSegments =
            [
                new EffectPatchSegment
                {
                    TargetFile = "Ekd5.exe",
                    AddressKind = "FileOffset",
                    Address = offset,
                    BytesHex = current,
                    ExpectedOldBytesHex = current,
                    HookPoint = "v7-authorization-smoke",
                    ModificationKind = EffectModificationKind.Maintenance,
                    SemanticFieldId = "registered-field:strategy-extension-marker",
                    SourceLocationId = "Ekd5.exe:000A2CE0:1",
                    RequiredCapability = EffectWriteCapability.RegisteredData
                }
            ]
        };
    }

    private static void RequirePreview(CczProject project, EffectPackage package)
    {
        var preview = new EffectTransactionalPatchService().Preview(project, package);
        if (!preview.CanApply || package.WriteAuthorization == null)
            throw new InvalidOperationException("v7 授权边界烟测无法生成有效预览：" + preview.Summary);
    }

    private static void AssertApplyRejected(CczProject project, EffectPackage package, string expectedMessage, string expectedSha)
    {
        try
        {
            new EffectTransactionalPatchService().Apply(project, package, "effect-v7-smoke", expectedReceiptOperationKind: null);
            throw new InvalidOperationException("无效结构化授权错误地通过了 Apply。");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal)) { }
        if (Sha(project.ResolveGameFile("Ekd5.exe")) != expectedSha)
            throw new InvalidOperationException("被拒绝的 Apply 修改了目标 EXE。");
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
