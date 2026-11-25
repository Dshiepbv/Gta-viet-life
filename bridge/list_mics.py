"""Liệt kê micro. Chọn số thứ tự của micro thật, ghi vào bridge/config.json: "mic_device": SỐ"""
import sounddevice as sd

default_in = sd.default.device[0]
for i, d in enumerate(sd.query_devices()):
    if d["max_input_channels"] > 0:
        mark = "  <-- đang dùng mặc định" if i == default_in else ""
        print(f"{i:3d}  {d['name']}  ({sd.query_hostapis(d['hostapi'])['name']}){mark}")
