"""下载固定版本 Horizon8，校验大小和 HF 对象身份。仅使用 Python 标准库和 curl。"""
import hashlib
import json
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

MODEL = "ARDY-Core-RP-20FPS-Horizon8"
REV = "b2a1749ab60ffca324bff11ea3012d4d8e1ac90b"
ROOT = Path(__file__).resolve().parents[2] / ".cache/ardy-horizon8"
DEST = ROOT / "checkpoints" / MODEL


def fetch(row):
    name = row["rfilename"]
    target = DEST / name
    if Path(name).is_absolute() or ":" in name or ".." in Path(name).parts:
        raise ValueError(f"无效路径: {name}")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.parent.resolve().relative_to(DEST.resolve())
    if target.is_symlink() or target.with_name(target.name + ".partial").is_symlink():
        raise ValueError(f"不接受链接: {name}")
    size = row["size"]
    part = target.with_name(target.name + ".partial")
    candidate = target if target.exists() else part
    if not target.exists() and (not part.exists() or part.stat().st_size < size):
        url = f"https://huggingface.co/nvidia/{MODEL}/resolve/{REV}/{name}?download=true&t={time.time_ns()}"
        subprocess.run(["curl.exe", "-fsSL", "--connect-timeout", "10", "--max-time", "600",
                        "--speed-limit", "1000", "--speed-time", "30", "--retry", "2",
                        "--continue-at", "-", url, "-o", str(part)], check=True)
    if candidate.stat().st_size != size:
        raise ValueError(f"{name}: 文件长度不匹配")
    sha = hashlib.sha256()
    blob = hashlib.sha1(f"blob {size}\0".encode())
    with candidate.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            sha.update(chunk)
            blob.update(chunk)
    lfs = row.get("lfs")
    verified = sha.hexdigest() == lfs["sha256"] if lfs else blob.hexdigest() == row["blobId"]
    if not verified:
        raise ValueError(f"{name}: 对象校验不匹配，保留原件供检查")
    if candidate == part:
        part.rename(target)
    print(f"verified {name} {size}", flush=True)
    return {"name": name, "bytes": size, "sha256": sha.hexdigest()}


if __name__ == "__main__":
    ROOT.mkdir(parents=True, exist_ok=True)
    meta = ROOT / "revision.json"
    subprocess.run(["curl.exe", "-fsSL", "--connect-timeout", "10", "--max-time", "40",
                    f"https://huggingface.co/api/models/nvidia/{MODEL}/revision/{REV}?blobs=true",
                    "-o", str(meta)], check=True)
    snapshot = json.loads(meta.read_bytes())
    rows = snapshot["siblings"]
    if snapshot["sha"] != REV or sum(r["size"] for r in rows) > 800_000_000:
        sys.exit("版本或大小超过固定下载边界")
    with ThreadPoolExecutor(max_workers=4) as pool:
        files = list(pool.map(fetch, rows))
    (ROOT / "checkpoint-verification.json").write_text(
        json.dumps({"revision": REV, "files": files}, indent=2), encoding="utf-8")
