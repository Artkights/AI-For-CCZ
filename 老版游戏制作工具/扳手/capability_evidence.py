"""Capability states and tamper-evident verification bundles for 6.6."""

from __future__ import annotations

import hashlib
import json
import os
import re
from dataclasses import dataclass, field, replace
from enum import Enum
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple


EVIDENCE_SCHEMA_VERSION = "1.0"
COLLECTOR_ID = "wrench-evidence-collector/1"
MANIFEST_NAME = "manifest.json"


class EvidenceError(RuntimeError):
    pass


class CapabilityState(str, Enum):
    UNAVAILABLE = "Unavailable"
    STATIC_VERIFIED = "StaticVerified"
    RUNTIME_VERIFIED = "RuntimeVerified"
    ENABLED = "Enabled"

    @property
    def rank(self) -> int:
        return {
            CapabilityState.UNAVAILABLE: 0,
            CapabilityState.STATIC_VERIFIED: 1,
            CapabilityState.RUNTIME_VERIFIED: 2,
            CapabilityState.ENABLED: 3,
        }[self]

    @property
    def label(self) -> str:
        return {
            CapabilityState.UNAVAILABLE: "禁用",
            CapabilityState.STATIC_VERIFIED: "仅静态确认",
            CapabilityState.RUNTIME_VERIFIED: "实机已验证",
            CapabilityState.ENABLED: "已启用",
        }[self]


@dataclass(frozen=True)
class FeatureCapability:
    key: str
    state: CapabilityState
    reason: str = ""
    reason_code: str = ""
    recipe_id: str = ""
    evidence_ids: Tuple[str, ...] = ()
    addresses: Dict[str, int] = field(default_factory=dict)
    recipe: Dict[str, Any] = field(default_factory=dict)

    @property
    def available(self) -> bool:
        """Compatibility surface used by the historic UI."""
        return self.state is CapabilityState.ENABLED

    @property
    def statically_verified(self) -> bool:
        return self.state.rank >= CapabilityState.STATIC_VERIFIED.rank

    @property
    def runtime_verified(self) -> bool:
        return self.state.rank >= CapabilityState.RUNTIME_VERIFIED.rank

    def with_state(self, state: CapabilityState, reason: Optional[str] = None,
                   reason_code: Optional[str] = None,
                   evidence_ids: Optional[Sequence[str]] = None) -> "FeatureCapability":
        return replace(
            self, state=CapabilityState(state),
            reason=self.reason if reason is None else reason,
            reason_code=self.reason_code if reason_code is None else reason_code,
            evidence_ids=(self.evidence_ids if evidence_ids is None
                          else tuple(evidence_ids)))

    def promote(self, state: CapabilityState,
                evidence_ids: Sequence[str] = ()) -> "FeatureCapability":
        state = CapabilityState(state)
        if state.rank < self.state.rank:
            raise ValueError("能力状态不能通过 promote 降级")
        if state.rank > self.state.rank + 1:
            raise ValueError("能力状态不能跳过验证阶段")
        if self.state is CapabilityState.UNAVAILABLE and not self.recipe_id:
            raise ValueError("没有静态配方的能力不能提升")
        return self.with_state(state, reason="", reason_code="",
                               evidence_ids=evidence_ids or self.evidence_ids)

    def disable(self, reason: str, reason_code: str) -> "FeatureCapability":
        return self.with_state(CapabilityState.UNAVAILABLE, reason=reason,
                               reason_code=reason_code)


@dataclass(frozen=True)
class VerifiedRecipe:
    feature_key: str
    recipe_id: str
    recipe: Dict[str, Any]
    evidence_ids: Tuple[str, ...]


RUNTIME_REQUIRED_CHECKS: Dict[str, Tuple[str, ...]] = {
    "control_all_factions": (
        "signature_unique", "instruction_boundaries", "oracle_match",
        "camp_matrix_complete", "rollback_verified", "three_toggles",
        "external_change_refused",
    ),
    "control_auto_return": (
        "signature_unique", "native_call_targets", "three_patch_sites",
        "stationary_wait_verified", "moved_wait_verified",
        "reselect_verified", "menu_verified", "safe_fields_unchanged",
        "battle_switch_verified", "rollback_verified", "three_toggles",
        "external_change_refused",
    ),
    "turn_refresh_direction": (
        "signature_unique", "abi_verified", "camp_matrix_complete",
        "direction_only", "rollback_verified",
    ),
    "turn_refresh_position": (
        "signature_unique", "abi_verified", "camp_matrix_complete",
        "map_occupancy", "collision", "pathfinding", "selection_refresh",
        "render_refresh", "rollback_verified",
    ),
    "item_consumables": (
        "static_formula", "boundary_150", "middle_id", "boundary_254",
        "category_matrix", "negative_controls", "write_255_restore",
        "rollback_verified",
    ),
}
GENERIC_RUNTIME_CHECKS = ("runtime_probe", "behavior_verified")


def canonical_json(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True,
                      separators=(",", ":")).encode("utf-8")


def sha256_json(value: Any) -> str:
    return hashlib.sha256(canonical_json(value)).hexdigest().upper()


def recipe_id(feature_key: str, recipe: Mapping[str, Any]) -> str:
    digest = sha256_json(recipe)[:20]
    return "ccz66.%s.%s" % (feature_key, digest)


def required_runtime_checks(feature_key: str) -> Tuple[str, ...]:
    return RUNTIME_REQUIRED_CHECKS.get(feature_key, GENERIC_RUNTIME_CHECKS)


def finalize_bundle(bundle: Mapping[str, Any]) -> Dict[str, Any]:
    result = dict(bundle)
    result.pop("bundleDigest", None)
    samples = result.get("runtimeSamples", [])
    result["rawSampleDigest"] = sha256_json(samples)
    result["bundleDigest"] = sha256_json(result)
    return result


def validate_bundle(bundle: Mapping[str, Any], runtime_required: bool = False) -> None:
    if bundle.get("schemaVersion") != EVIDENCE_SCHEMA_VERSION:
        raise EvidenceError("证据 schemaVersion 不受支持")
    if not bundle.get("featureKey") or not bundle.get("recipeId"):
        raise EvidenceError("证据缺少功能键或配方 ID")
    target = bundle.get("target")
    if not isinstance(target, Mapping) or not re.fullmatch(
            r"[0-9A-Fa-f]{64}", str(target.get("sha256", ""))):
        raise EvidenceError("证据目标 SHA256 无效")
    if int(target.get("fileSize", 0)) <= 0:
        raise EvidenceError("证据目标文件长度无效")
    expected = dict(bundle)
    digest = str(expected.pop("bundleDigest", "")).upper()
    if not digest or sha256_json(expected) != digest:
        raise EvidenceError("证据内容摘要不匹配")
    samples = bundle.get("runtimeSamples", [])
    if sha256_json(samples) != str(bundle.get("rawSampleDigest", "")).upper():
        raise EvidenceError("证据原始样本摘要不匹配")
    if not runtime_required:
        return
    if bundle.get("phase") != "runtime":
        raise EvidenceError("证据不是运行时阶段")
    verification = bundle.get("verification")
    if not isinstance(verification, Mapping):
        raise EvidenceError("运行时证据缺少验证结果")
    if verification.get("collector") != COLLECTOR_ID:
        raise EvidenceError("运行时证据不是由受信采集器生成")
    if verification.get("status") != "verified" or verification.get("automated") is not True:
        raise EvidenceError("运行时证据没有通过自动验证")
    checks = verification.get("checks")
    if not isinstance(checks, Mapping):
        raise EvidenceError("运行时证据缺少自动检查")
    missing = [name for name in required_runtime_checks(
        str(bundle["featureKey"])) if checks.get(name) is not True]
    if missing:
        raise EvidenceError("运行时证据未通过: " + ", ".join(missing))
    if not samples:
        raise EvidenceError("运行时证据没有原始样本")


class EvidenceRepository:
    def __init__(self, root: Optional[os.PathLike] = None):
        self.root = Path(root) if root is not None else Path(__file__).with_name("evidence")

    @property
    def manifest_path(self) -> Path:
        return self.root / MANIFEST_NAME

    def _manifest_files(self) -> Dict[str, str]:
        if not self.manifest_path.is_file():
            return {}
        try:
            manifest = json.loads(self.manifest_path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            raise EvidenceError("证据清单无法读取: %s" % exc) from exc
        if manifest.get("schemaVersion") != EVIDENCE_SCHEMA_VERSION:
            raise EvidenceError("证据清单版本不受支持")
        result: Dict[str, str] = {}
        for item in manifest.get("files", []):
            path = str(item.get("path", ""))
            digest = str(item.get("sha256", "")).upper()
            if not path or Path(path).is_absolute() or ".." in Path(path).parts:
                raise EvidenceError("证据清单包含非法路径")
            if not re.fullmatch(r"[0-9A-F]{64}", digest):
                raise EvidenceError("证据清单包含非法摘要")
            result[path] = digest
        return result

    def load_bundles(self, runtime_only: bool = False) -> List[Dict[str, Any]]:
        bundles: List[Dict[str, Any]] = []
        for relative, expected_digest in self._manifest_files().items():
            path = self.root / relative
            try:
                raw = path.read_bytes()
            except OSError as exc:
                raise EvidenceError("证据文件缺失: %s" % relative) from exc
            if hashlib.sha256(raw).hexdigest().upper() != expected_digest:
                raise EvidenceError("证据文件清单摘要不匹配: %s" % relative)
            try:
                bundle = json.loads(raw.decode("utf-8"))
                validate_bundle(bundle, runtime_required=runtime_only)
            except (UnicodeError, ValueError, EvidenceError) as exc:
                if runtime_only:
                    continue
                raise EvidenceError("证据文件无效 %s: %s" % (relative, exc)) from exc
            if not runtime_only or bundle.get("phase") == "runtime":
                bundles.append(bundle)
        return bundles

    def verified_recipe(self, feature_key: str,
                        required_sha256: Sequence[str]) -> Optional[VerifiedRecipe]:
        required = {item.upper() for item in required_sha256}
        candidates: Dict[str, Dict[str, Dict[str, Any]]] = {}
        for bundle in self.load_bundles(runtime_only=True):
            if bundle.get("featureKey") != feature_key:
                continue
            sha = str(bundle["target"]["sha256"]).upper()
            if sha not in required:
                continue
            candidates.setdefault(str(bundle["recipeId"]), {})[sha] = bundle
        for current_recipe_id, by_sha in candidates.items():
            if set(by_sha) != required:
                continue
            recipes = [bundle.get("recipe") for bundle in by_sha.values()]
            if not all(isinstance(item, Mapping) for item in recipes):
                continue
            first = canonical_json(recipes[0])
            if any(canonical_json(item) != first for item in recipes[1:]):
                continue
            if recipe_id(feature_key, recipes[0]) != current_recipe_id:
                continue
            evidence_ids = tuple(
                "%s:%s" % (sha, by_sha[sha]["bundleDigest"][:16])
                for sha in sorted(required))
            return VerifiedRecipe(feature_key, current_recipe_id,
                                  dict(recipes[0]), evidence_ids)
        return None

    def write_bundle(self, bundle: Mapping[str, Any]) -> Path:
        finalized = finalize_bundle(bundle)
        validate_bundle(finalized, runtime_required=(finalized.get("phase") == "runtime"
                                                      and finalized.get("verification", {}).get(
                                                          "status") == "verified"))
        self.root.mkdir(parents=True, exist_ok=True)
        safe_feature = re.sub(r"[^A-Za-z0-9_.-]", "_",
                              str(finalized["featureKey"]))
        sha = str(finalized["target"]["sha256"]).upper()
        phase = str(finalized.get("phase", "unknown"))
        filename = "%s-%s-%s.json" % (safe_feature, sha[:12], phase)
        path = self.root / filename
        path.write_text(json.dumps(finalized, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8")
        self.rebuild_manifest()
        return path

    def rebuild_manifest(self) -> None:
        self.root.mkdir(parents=True, exist_ok=True)
        files = []
        for path in sorted(self.root.glob("*.json")):
            if path.name == MANIFEST_NAME:
                continue
            files.append({
                "path": path.name,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper(),
            })
        manifest = {"schemaVersion": EVIDENCE_SCHEMA_VERSION, "files": files}
        temporary = self.manifest_path.with_suffix(".tmp")
        temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
                             encoding="utf-8")
        os.replace(str(temporary), str(self.manifest_path))


def make_bundle(feature_key: str, target_sha256: str, file_size: int,
                current_recipe_id: str, recipe: Mapping[str, Any], phase: str,
                static_evidence: Mapping[str, Any],
                runtime_samples: Iterable[Mapping[str, Any]] = (),
                verification: Optional[Mapping[str, Any]] = None) -> Dict[str, Any]:
    return finalize_bundle({
        "schemaVersion": EVIDENCE_SCHEMA_VERSION,
        "featureKey": feature_key,
        "target": {"sha256": target_sha256.upper(), "fileSize": int(file_size)},
        "recipeId": current_recipe_id,
        "recipe": dict(recipe),
        "phase": phase,
        "staticEvidence": dict(static_evidence),
        "runtimeSamples": list(runtime_samples),
        "verification": dict(verification or {
            "collector": COLLECTOR_ID,
            "status": "captured",
            "automated": True,
            "checks": {},
        }),
    })
