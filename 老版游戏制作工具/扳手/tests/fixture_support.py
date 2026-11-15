import os
from pathlib import Path


def _fixture_workspace():
    configured = os.environ.get("BANSHOU_TEST_WORKSPACE")
    if configured:
        return Path(configured).expanduser().resolve()
    test_file = Path(__file__).resolve()
    candidates = (test_file.parents[2], test_file.parents[3])
    for candidate in candidates:
        if (candidate / "基底").is_dir():
            return candidate
    return candidates[0]


WORKSPACE = _fixture_workspace()
HAS_FIXTURES = (WORKSPACE / "基底").is_dir()
FIXTURE_SKIP_REASON = (
    "本地引擎样本不可用；设置 BANSHOU_TEST_WORKSPACE 指向包含‘基底’目录的工作区"
)
