"""Tự tìm micro đúng: thu đồng thời trên mọi micro 4 giây, micro nào nghe thấy giọng to nhất sẽ được ghi vào config.json."""
import json
import os
import sys
import time

import numpy as np
import sounddevice as sd

HERE = os.path.dirname(os.path.abspath(__file__))
RATE = 16000

devs = sd.query_devices()
mme = [i for i, d in enumerate(devs)
       if d["max_input_channels"] > 0 and sd.query_hostapis(d["hostapi"])["name"] == "MME" and "Mapper" not in d["name"]]
if not mme:
    print("Không thấy micro nào."); sys.exit(1)

peaks, streams = {}, []


def make_cb(i):
    def cb(indata, frames, t, status):
        peaks[i] = max(peaks.get(i, 0.0), float(np.max(np.abs(indata))))
    return cb


for i in mme:
    try:
        s = sd.InputStream(samplerate=RATE, channels=1, dtype="float32", device=i, callback=make_cb(i))
        s.start(); streams.append(s); peaks[i] = 0.0
    except Exception as e:
        print(f"  bỏ qua {i} {devs[i]['name']}: {e}")

print("\n>>> HÃY NÓI TO, LIÊN TỤC trong 4 giây: 'một hai ba bốn năm sáu bảy tám chín mười' <<<\n")
time.sleep(4.5)
for s in streams:
    s.stop(); s.close()

best = max(peaks, key=peaks.get)
for i in sorted(peaks, key=peaks.get, reverse=True):
    print(f"{i:3d}  đỉnh {peaks[i]:.3f}  {devs[i]['name']}")
print()
if peaks[best] < 0.02:
    print("Không micro nào nghe thấy tiếng. Kiểm tra micro đã cắm/bật chưa, và Windows > Sound settings > Input volume.")
    sys.exit(2)

path = os.path.join(HERE, "config.json")
cfg = json.load(open(path, encoding="utf-8"))
cfg["mic_device"] = best
json.dump(cfg, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print(f"==> Đã chọn micro số {best}: {devs[best]['name']}  (ghi vào config.json)")
