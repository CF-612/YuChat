"""在有基础模型访问权的外部 Linux GPU 上，使用已安装的官方 ARDY 环境导出三条向量。"""
import argparse
import json
from pathlib import Path
import platform
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("upstream", type=Path, help="已安装依赖的官方 ARDY 源码目录")
    parser.add_argument("output", type=Path, help="新的向量包目录")
    args = parser.parse_args()
    if platform.system() != "Linux" or "microsoft" in platform.release().lower():
        parser.error("此入口仅供外部 Linux 编码机使用")
    if args.output.exists():
        parser.error("输出目录必须是新目录")
    spec = json.loads(Path(__file__).with_name("intent-inputs.json").read_text(encoding="utf-8"))
    source = args.upstream.resolve()
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != spec["upstream"]:
        parser.error("ARDY 版本与输入清单不一致")
    from huggingface_hub import HfApi, snapshot_download
    import torch
    if not torch.cuda.is_available():
        parser.error("外部编码机需要 CUDA")
    encoder = spec["encoder"]
    arguments = [sys.executable, str(Path(__file__).with_name("intent_pack.py")),
                 "export", str(args.output), "--upstream", str(source)]
    for name, repo in (("llama", "meta-llama/Meta-Llama-3-8B-Instruct"),
                       ("base", encoder["base"]), ("adapter", encoder["adapter"])):
        expected = encoder[name + "Revision"]
        # 底模由上游按仓库名加载，HF 的 main 引用必须指向经过核对的 snapshot。
        ref = "main" if name == "llama" else expected
        if HfApi().model_info(repo, revision=ref).sha != expected:
            parser.error(f"{repo} 版本已变化，请使用已有固定 snapshot 和 intent_pack.py export")
        path = Path(snapshot_download(repo, revision=ref, allow_patterns=[
            "*.json", "*.safetensors", "tokenizer.model", "*.txt", "*.model",
        ], ignore_patterns=["original/*"]))
        if path.name != expected:
            raise ValueError(f"下载期间版本变化: {repo}")
        arguments.extend(["--" + name, str(path)])
    subprocess.run(arguments, check=True)


if __name__ == "__main__":
    main()
