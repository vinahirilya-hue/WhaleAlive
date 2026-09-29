"""Expand and normalize the user's finished animation source folders.

Each action becomes one folder containing only ``动作名-01.png`` style final
frames. An animation.json is used once to preserve its declared order and
exclusions, then removed with reference images and other export metadata.
Original ZIP exports are retained under 原始压缩包.
"""

from __future__ import annotations

import json
import re
import shutil
import tempfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "characters" / "成品序列帧"
ARCHIVES = SOURCE / "原始压缩包"


def natural(value: str) -> list[object]:
    return [int(part) if part.isdigit() else part.casefold() for part in re.split(r"(\d+)", value)]


def safe_extract(archive: Path, destination: Path) -> None:
    root = destination.resolve()
    with zipfile.ZipFile(archive) as source:
        for member in source.infolist():
            target = (destination / member.filename).resolve()
            if root != target and root not in target.parents:
                raise ValueError(f"压缩包包含越界路径：{archive.name} -> {member.filename}")
        source.extractall(destination)


def metadata_path(folder: Path) -> Path | None:
    matches = list(folder.rglob("animation.json"))
    if len(matches) > 1:
        raise ValueError(f"{folder.name} 中存在多个 animation.json")
    return matches[0] if matches else None


def ordered_frames(folder: Path, metadata: dict) -> list[Path]:
    pngs = [path for path in folder.rglob("*.png") if "reference" not in path.name.casefold()]
    by_relative = {path.relative_to(folder).as_posix(): path for path in pngs}
    if metadata.get("frames"):
        ordered = []
        for entry in metadata["frames"]:
            key = str(entry["file"]).replace("\\", "/")
            if key not in by_relative:
                raise FileNotFoundError(f"{folder.name} 缺少清单帧：{key}")
            ordered.append(by_relative[key])
        return ordered
    return sorted(pngs, key=lambda path: natural(path.relative_to(folder).as_posix()))


def normalize(folder: Path) -> int:
    manifest = metadata_path(folder)
    metadata = json.loads(manifest.read_text(encoding="utf-8-sig")) if manifest else {}
    frames = ordered_frames(folder, metadata)
    if not frames:
        raise ValueError(f"{folder.name} 没有 PNG 帧")

    temporary: list[Path] = []
    for index, source in enumerate(frames, 1):
        target = folder / f".__frame-{index:04d}.png"
        source.replace(target)
        temporary.append(target)

    width = max(2, len(str(len(temporary))))
    names: list[str] = []
    for index, source in enumerate(temporary, 1):
        name = f"{folder.name}-{index:0{width}d}.png"
        source.replace(folder / name)
        names.append(name)

    retained = {folder / name for name in names}
    for child in folder.rglob("*"):
        if child.is_file() and child not in retained:
            child.unlink()

    for child in sorted(folder.rglob("*"), key=lambda path: len(path.parts), reverse=True):
        if child.is_dir() and not any(child.iterdir()):
            child.rmdir()
    return len(names)


def main() -> None:
    SOURCE.mkdir(parents=True, exist_ok=True)
    ARCHIVES.mkdir(exist_ok=True)

    for archive in sorted(SOURCE.glob("*.zip"), key=lambda path: natural(path.name)):
        destination = SOURCE / archive.stem
        if destination.exists():
            raise FileExistsError(f"目标文件夹已存在，拒绝覆盖：{destination}")
        with tempfile.TemporaryDirectory(prefix="whale-sequence-") as temp:
            extracted = Path(temp)
            safe_extract(archive, extracted)
            shutil.move(str(extracted), str(destination))
        shutil.move(str(archive), str(ARCHIVES / archive.name))

    results = []
    for folder in sorted(
        (path for path in SOURCE.iterdir() if path.is_dir() and path != ARCHIVES),
        key=lambda path: natural(path.name),
    ):
        results.append((folder.name, normalize(folder)))

    for name, count in results:
        print(f"{name}: {count} 帧")
    print(f"共整理 {len(results)} 个动作、{sum(count for _, count in results)} 帧")


if __name__ == "__main__":
    main()
