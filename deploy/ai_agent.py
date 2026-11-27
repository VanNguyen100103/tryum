#!/usr/bin/env python3
"""AI dev agent (free tier): sinh unit test xUnit cho một file nguồn bằng Groq.

Workflow dùng script này:
  1. Chạy script -> tạo file test trong backend/tests/Tryum.UnitTests/AiGenerated/
  2. Gate: dotnet build + dotnet test
  3. Pass -> commit + deploy; Fail -> xoá file, không commit

Env:
  GROQ_API_KEY   (bắt buộc)
  AI_TARGET      file nguồn (mặc định PhoneNormalizer)
  AI_OUT         file test đầu ra
  AI_MODEL       model Groq (mặc định openai/gpt-oss-120b)
"""

import json
import os
import re
import sys
import urllib.error
import urllib.request

GROQ_URL = "https://api.groq.com/openai/v1/chat/completions"
KEY = os.environ.get("GROQ_API_KEY", "").strip()
TARGET = os.environ.get("AI_TARGET", "backend/src/Tryum.Application/Common/PhoneNormalizer.cs")
OUT = os.environ.get("AI_OUT", "backend/tests/Tryum.UnitTests/AiGenerated/PhoneNormalizerAiTests.cs")
MODEL = os.environ.get("AI_MODEL", "openai/gpt-oss-120b")

if not KEY:
    print("Thiếu GROQ_API_KEY")
    sys.exit(1)

if not os.path.exists(TARGET):
    print(f"Không thấy file nguồn: {TARGET}")
    sys.exit(1)

source = open(TARGET, encoding="utf-8").read()

SYSTEM = """Bạn là kỹ sư .NET viết unit test xUnit cho một file C#.
Chỉ trả về NỘI DUNG file .cs hoàn chỉnh. KHÔNG markdown, KHÔNG giải thích, KHÔNG ``` .

Yêu cầu bắt buộc:
- Dòng đầu là: using Xunit;
- namespace Tryum.UnitTests.AiGenerated
- public class <TênLớp>AiTests
- Mỗi test dùng [Fact], tên phương thức rõ nghĩa tiếng Anh
- CHỈ gọi API public có thật trong file nguồn
- Bao gồm cả trường hợp hợp lệ và không hợp lệ
- Không dùng package ngoài xUnit
- Code phải compile được với C# 13 / .NET 10"""

USER = f"File nguồn:\n\n{source}\n\nHãy viết unit test cho các hàm public trong file này."

payload = json.dumps({
    "model": MODEL,
    "messages": [
        {"role": "system", "content": SYSTEM},
        {"role": "user", "content": USER},
    ],
    "temperature": 0.2,
    "max_tokens": 2000,
}).encode()

req = urllib.request.Request(GROQ_URL, data=payload, method="POST")
req.add_header("Content-Type", "application/json")
req.add_header("Authorization", f"Bearer {KEY}")
# Cloudflare (Groq) chặn User-Agent mặc định của Python -> phải đặt UA rõ ràng.
req.add_header("User-Agent", "TryumAIAgent/1.0 (+https://github.com/VanNguyen100103/tryum)")

try:
    with urllib.request.urlopen(req, timeout=90) as resp:
        data = json.loads(resp.read())
except urllib.error.HTTPError as exc:
    print(f"Groq HTTP {exc.code}: {exc.read()[:300]}")
    sys.exit(1)
except Exception as exc:
    print(f"Groq lỗi: {exc}")
    sys.exit(1)

content = data["choices"][0]["message"]["content"].strip()

# bỏ markdown fence nếu model vẫn thêm
content = re.sub(r"^```(?:csharp|cs)?\s*", "", content)
content = re.sub(r"\s*```$", "", content)
content = content.strip()

if "using Xunit;" not in content or "namespace Tryum.UnitTests.AiGenerated" not in content:
    print("Output không đúng định dạng (thiếu using Xunit / namespace). Bỏ qua.")
    print(content[:400])
    sys.exit(1)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, "w", encoding="utf-8").write(content + "\n")
print(f"Đã ghi {OUT} ({len(content)} ký tự)")
