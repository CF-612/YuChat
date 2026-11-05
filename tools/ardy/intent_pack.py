"""外部编码机生成，目标机仅校验。两个入口都不自动下载模型。"""
import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile

import numpy as np

SPEC = json.loads(Path(__file__).with_name("intent-inputs.json").read_text(encoding="utf-8"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_snapshot(path, repo_id):
    path = path.resolve()
    if (path.parent.name != "snapshots" or
            path.parent.parent.name != "models--" + repo_id.replace("/", "--") or
            not re.fullmatch(r"[0-9a-f]{40}", path.name) or not path.is_dir()):
        raise ValueError(f"请提供 {repo_id} 的已缓存 Hugging Face snapshot 目录")
    return path


def validate(folder):
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    for field in ("schemaVersion", "upstream", "checkpoint", "encoder"):
        if manifest[field] != SPEC[field]:
            raise ValueError(f"来源不匹配: {field}")
    for field in ("baseRevision", "adapterRevision", "llamaRevision"):
        if manifest[field] != SPEC["encoder"][field]:
            raise ValueError(f"编码器版本不匹配: {field}")
    expected = {v["id"]: v for v in SPEC["intents"]}
    seen = set()
    for row in manifest["intents"]:
        spec = expected[row["id"]]
        if row.get("status") != "encoded" or row["id"] in seen or any(row[k] != spec[k] for k in ("prompt", "file", "fallback", "stage")):
            raise ValueError(f"重复或不匹配的意图: {row['id']}")
        seen.add(row["id"])
        path = folder / spec["file"]
        if path.stat().st_size > 32768 or digest(path) != row["sha256"]:
            raise ValueError(f"文件校验失败: {path.name}")
        arr = np.load(path, allow_pickle=False)
        if (arr.shape != (1, 4096) or arr.dtype != np.float32 or not np.isfinite(arr).all()
                or not arr.any() or row["shape"] != list(arr.shape) or row["dtype"] != str(arr.dtype)):
            raise ValueError(f"向量无效: {path.name}")
    if not {v["id"] for v in expected.values() if v["stage"] == "first"} <= seen:
        raise ValueError("首测三条向量不齐")
    print(f"validated {len(seen)} intent files")


def export(args):
    # 显式离线，避免缺少 snapshot 时在目标 PC 或外部机意外拉取大文件。
    os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1")
    if os.environ.get("TEXT_ENCODERS_DIR"):
        raise ValueError("请取消 TEXT_ENCODERS_DIR，使用命令传入的固定 snapshot")
    source = args.upstream.resolve()
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != SPEC["upstream"] or subprocess.check_output(
            ["git", "-C", str(source), "status", "--porcelain", "--", "ardy"]):
        raise ValueError("ARDY 源码版本或工作树与清单不一致")
    base = read_snapshot(args.base, SPEC["encoder"]["base"])
    adapter = read_snapshot(args.adapter, SPEC["encoder"]["adapter"])
    llama = read_snapshot(args.llama, "meta-llama/Meta-Llama-3-8B-Instruct")
    # 上游 adapter 的配置按仓库名解析基础 Llama。固定离线 hub 和 main 引用。
    if base.parents[2] != llama.parents[2] or (llama.parent.parent / "refs/main").read_text().strip() != llama.name:
        raise ValueError("基础 Llama 必须能在同一离线 Hub 按给定 revision 解析")
    os.environ["HF_HUB_CACHE"] = str(llama.parents[2])
    import torch
    sys.path.insert(0, str(source))
    from ardy.model.llm2vec.llm2vec_wrapper import LLM2VecEncoder
    encoder = LLM2VecEncoder(str(base), str(adapter), "bfloat16", 4096, device="cuda")
    manifest = {k: SPEC[k] for k in ("schemaVersion", "upstream", "checkpoint", "encoder")}
    manifest.update(baseRevision=base.name, adapterRevision=adapter.name, llamaRevision=llama.name,
                    packages={k: importlib.metadata.version(k) for k in ("torch", "transformers", "peft")},
                    intents=[])
    if args.folder.exists():
        raise FileExistsError("输出目录须为新目录")
    args.folder.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.folder.parent) as temporary:
        staged = Path(temporary) / "pack"
        staged.mkdir()
        for row in SPEC["intents"]:
            if row["stage"] != "first" and not args.all:
                continue
            tensor, lengths = encoder([row["prompt"]])
            if list(tensor.shape) != [1, 1, 4096] or list(lengths) != [1]:
                raise ValueError(f"编码器输出不符合契约: {row['id']}")
            arr = tensor[0].detach().to(device="cpu", dtype=torch.float32).numpy()
            path = staged / row["file"]
            np.save(path, arr, allow_pickle=False)
            manifest["intents"].append(dict(row, status="encoded", shape=list(arr.shape),
                                            dtype=str(arr.dtype), sha256=digest(path)))
        (staged / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
        validate(staged)
        staged.rename(args.folder)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    check = modes.add_parser("verify")
    check.add_argument("folder", type=Path)
    gen = modes.add_parser("export")
    gen.add_argument("folder", type=Path)
    for option in ("upstream", "base", "adapter", "llama"):
        gen.add_argument("--" + option, type=Path, required=True)
    gen.add_argument("--all", action="store_true")
    args = parser.parse_args()
    if args.mode == "verify":
        validate(args.folder)
    else:
        export(args)
