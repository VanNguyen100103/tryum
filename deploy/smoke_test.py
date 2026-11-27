#!/usr/bin/env python3
"""Smoke test production cho Tryum — chạy trong CI/cron, exit 1 nếu bất kỳ check fail.

Kiểm tra:
  1. /health                      -> 200 + body "Healthy"
  2. /api/v1/auth/stores/{slug}   -> 200 + JSON có slug
  3. POST /api/v1/auth/login sai  -> 401/400 (chứng minh pipeline auth sống, không 500)
  4. thời gian phản hồi dưới ngưỡng

Biến môi trường:
  API_BASE_URL  (mặc định https://tryum-api.duckdns.org)
  SMOKE_SLUG    (mặc định banh-xeo-a-van)
  SMOKE_MAX_MS  (mặc định 3000)
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request

API = os.environ.get("API_BASE_URL", "https://tryum-api.duckdns.org").rstrip("/")
SLUG = os.environ.get("SMOKE_SLUG", "banh-xeo-a-van")
MAX_MS = int(os.environ.get("SMOKE_MAX_MS", "3000"))

failures: list[str] = []


def request(path: str, method: str = "GET", payload: dict | None = None, expect: int = 200):
    url = API + path
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")

    started = time.time()
    try:
        with urllib.request.urlopen(req, timeout=20) as resp:
            code, body = resp.status, resp.read()
    except urllib.error.HTTPError as exc:
        code, body = exc.code, exc.read()
    except Exception as exc:  # timeout, DNS, connection refused...
        failures.append(f"{path}: {exc}")
        print(f"FAIL {path}: {exc}")
        return None, 0

    ms = int((time.time() - started) * 1000)

    if code != expect:
        failures.append(f"{path}: HTTP {code} (mong đợi {expect})")
        print(f"FAIL {path}: HTTP {code} trong {ms}ms")
    elif ms > MAX_MS:
        failures.append(f"{path}: {ms}ms > {MAX_MS}ms")
        print(f"SLOW {path}: {ms}ms (ngưỡng {MAX_MS}ms)")
    else:
        print(f"OK   {path}: HTTP {code} {ms}ms")

    return body, ms


# 1) health
body, _ = request("/health")
if body and b"Healthy" not in body:
    failures.append("/health: body không chứa 'Healthy'")
    print("FAIL /health: body sai")

# 2) store lookup (public) + JSON hợp lệ
body, _ = request(f"/api/v1/auth/stores/{SLUG}")
if body:
    try:
        data = json.loads(body)
        if not data.get("slug"):
            failures.append("stores: JSON thiếu 'slug'")
            print("FAIL stores: thiếu slug")
        else:
            print(f"     store = {data.get('tenantName')} ({data.get('slug')})")
    except Exception as exc:
        failures.append(f"stores: JSON không hợp lệ ({exc})")
        print(f"FAIL stores: JSON lỗi {exc}")

# 3) login sai -> 401 (endpoint sống, không 500)
request(
    "/api/v1/auth/login",
    method="POST",
    payload={"identifier": "smoke-test@invalid.local", "password": "wrong-password", "tenantSlug": None},
    expect=401,
)

# 4) tổng kết
print()
if failures:
    print(f"❌ {len(failures)} check FAIL:")
    for item in failures:
        print(f"   - {item}")
    sys.exit(1)

print("✅ Tất cả smoke test PASS")
