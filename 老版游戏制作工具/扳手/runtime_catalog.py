"""Read the current process's item catalog, independently of disk resources.

This module does not attach to a process or write memory.  The caller supplies
the existing session, and a machine-code-resolved table base and codec are
required.  Extended consumable IDs are a separate domain from this catalog.
"""

from capability_evidence import CapabilityState, FeatureCapability, recipe_id
from engine_layout import FeatureAccess, ResolvedFeature
from engine_profile import (ITEM_CODEC_65, ITEM_CODEC_66, ITEM_CODEC_LEGACY,
                            ITEM_RECORD_COUNT_66,
                            ITEM_ROW_SIZE_66, ProfileError,
                            parse_item_row, validate_item_table_66)
from process_memory import MemoryAccessError


def hydrate_runtime_item_catalog(profile, memory) -> bool:
    """Refresh metadata from the attached engine's proven work area.

    The static image often reserves this table as zero-filled data, so disk
    bytes cannot supply it.  Do not substitute another game's Data/Star files.
    A failed or uninitialized runtime table leaves the original diagnostic in
    place and lets the other pages continue.
    """
    if memory is None:
        return False
    codec = profile.metadata.get("item_row_codec")
    base = int(profile.addresses.get("item_table", 0))
    layout = profile.metadata.get("item_layout", {})
    if (codec not in (ITEM_CODEC_65, ITEM_CODEC_66, ITEM_CODEC_LEGACY) or base <= 0 or
            not isinstance(layout, dict) or
            (int(layout.get("lookupAddress", 0)) <= 0 and
             not layout.get("legacyRuntimeContract"))):
        return False
    record_count = int(profile.limits.get(
        "item_record_count", ITEM_RECORD_COUNT_66))
    if not 1 <= record_count <= 512:
        return False
    size = record_count * ITEM_ROW_SIZE_66
    try:
        if not memory.validate_range(base, size, write=False):
            raise MemoryAccessError("运行时物品元数据范围不可读")
        raw = memory.read_bytes(base, size)
        # Equipment fields use byte IDs and the existing validated 255-row
        # codec.  Extended metadata names never enter that write domain.
        table_count = min(record_count, ITEM_RECORD_COUNT_66)
        table = validate_item_table_66(
            raw[:table_count * ITEM_ROW_SIZE_66], codec, table_count)
        names = tuple(parse_item_row(
            raw[index * ITEM_ROW_SIZE_66:(index + 1) * ITEM_ROW_SIZE_66],
            codec).name for index in range(record_count))
    except (MemoryAccessError, ProfileError, ValueError) as exc:
        profile.metadata["runtime_item_catalog_error"] = str(exc)
        profile.metadata.pop("item_table_66", None)
        profile.metadata.pop("runtime_item_names", None)
        profile.capabilities["item_metadata"] = FeatureCapability(
            "item_metadata", CapabilityState.UNAVAILABLE, str(exc),
            "RUNTIME_ITEM_CATALOG_UNREADABLE")
        profile.resolved_features["item_metadata"] = ResolvedFeature(
            "item_metadata", FeatureAccess.UNAVAILABLE, reason=str(exc),
            reason_code="RUNTIME_ITEM_CATALOG_UNREADABLE")
        if "item_bulk_treasure" in profile.capabilities:
            profile.capabilities["item_bulk_treasure"] = FeatureCapability(
                "item_bulk_treasure", CapabilityState.UNAVAILABLE, str(exc),
                "RUNTIME_ITEM_CATALOG_UNREADABLE")
        return False

    recipe = {
        "kind": "runtime-item-catalog", "runtimeTableBase": base,
        "lookupAddress": int(layout.get("lookupAddress", 0)), "codec": codec,
        "recordCount": record_count, "rowSize": ITEM_ROW_SIZE_66,
        "sessionGeneration": str(getattr(memory, "session_generation", "")),
    }
    previous_table = profile.metadata.get("item_table_66")
    previous_source = profile.metadata.get("item_catalog_source")
    previous_names = profile.metadata.get("runtime_item_names")
    if (previous_table == table and previous_source == recipe and
            previous_names == names):
        return False
    reason = "物品元数据来自当前进程的已定位表，只读解析"
    profile.metadata["item_table_66"] = table
    profile.metadata["runtime_item_names"] = names
    profile.metadata["item_catalog_source"] = dict(recipe)
    profile.metadata.pop("runtime_item_catalog_error", None)
    capability = FeatureCapability(
        "item_metadata", CapabilityState.STATIC_VERIFIED,
        reason=reason, recipe_id=recipe_id("item_metadata", recipe),
        recipe=recipe)
    profile.capabilities["item_metadata"] = capability
    profile.resolved_features["item_metadata"] = ResolvedFeature(
        "item_metadata", FeatureAccess.READ_ONLY, capability.recipe_id,
        resolved_address=base, reason=reason, runtime_probe_passed=True)
    # The button still obeys its warehouse dependency during probe_runtime.
    bulk = profile.capabilities.get("item_bulk_treasure")
    if bulk is not None:
        profile.capabilities["item_bulk_treasure"] = FeatureCapability(
            "item_bulk_treasure", CapabilityState.STATIC_VERIFIED,
            reason=reason, recipe_id=recipe_id("item_bulk_treasure", recipe),
            recipe=recipe)
    return True
