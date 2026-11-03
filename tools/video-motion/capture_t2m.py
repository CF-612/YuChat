"""Capture one uncached my-neuro T2M generation on the local PC."""
import argparse
import json
import math
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import ProxyHandler, Request, build_opener


def validate(data):
    frames, fps = data["joints"], data["fps"]
    if fps != 20 or not 2 <= len(frames) <= 196:
        raise ValueError("Expected 2–196 frames at 20 Hz")
    for frame in frames:
        if len(frame) != 22 or any(len(p) != 3 for p in frame):
            raise ValueError("Expected 22 XYZ joints per frame")
        for point in frame:
            if any(type(v) not in (float, int) or not math.isfinite(v) or abs(v) > 1000 for v in point):
                raise ValueError("Invalid joint coordinate")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prompt")
    parser.add_argument("output", type=Path)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--port", type=int, default=17880)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    body = {"text": args.prompt, "seed": args.seed}
    report = dict(body, model="morelle/T2M-DLC2", modelRevision="fc4221d11a9c31598b15feece5ab015e41d4f373")
    request = Request(f"http://127.0.0.1:{args.port}/api/generate", json.dumps(body).encode(),
                      {"Content-Type": "application/json"})
    start = time.perf_counter()
    try:
        with build_opener(ProxyHandler({})).open(request, timeout=120) as response:
            raw = response.read(5 * 1024 * 1024 + 1)
        elapsed = time.perf_counter() - start
        if len(raw) > 5 * 1024 * 1024:
            raise ValueError("Oversized motion")
        data = json.loads(raw)
        validate(data)
        (args.output / "motion.json").write_bytes(raw)
        report.update(status="ok", requestSeconds=elapsed, frames=len(data["joints"]), fps=data["fps"])
    except (HTTPError, URLError, TimeoutError, ValueError, KeyError, TypeError) as error:
        report.update(status="failed", error=str(error), failureSeconds=time.perf_counter()-start)
    finally:
        (args.output / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))
    return int(report["status"] != "ok")


if __name__ == "__main__":
    raise SystemExit(main())
