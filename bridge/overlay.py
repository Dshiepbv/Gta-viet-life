"""Phụ đề tiếng Việt nổi trên game (font GTA không có dấu tiếng Việt).

Cần chạy game ở chế độ "Borderless / Windowed Borderless" để cửa sổ này nổi lên trên.
"""
import queue
import tkinter as tk

KEY = "#010101"  # màu nền trong suốt


class Overlay:
    def __init__(self, cfg: dict):
        self.q = queue.Queue()
        self.root = tk.Tk()
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)
        try:
            self.root.attributes("-transparentcolor", KEY)
        except tk.TclError:
            pass
        self.root.configure(bg=KEY)
        sw, sh = self.root.winfo_screenwidth(), self.root.winfo_screenheight()
        w, h = int(sw * 0.7), 150
        self.root.geometry(f"{w}x{h}+{(sw - w) // 2}+{sh - h - int(cfg.get('overlay_bottom_px', 90))}")
        size = int(cfg.get("overlay_font_size", 22))
        common = dict(bg=KEY, font=("Segoe UI", size, "bold"), wraplength=w - 20, justify="center")
        self.player = tk.Label(self.root, fg="#9fd3ff", **common)
        self.npc = tk.Label(self.root, fg="#ffffff", **common)
        self.player.pack(fill="x")
        self.npc.pack(fill="x")
        self._clear_job = None
        self.root.after(100, self._poll)

    def show(self, who: str, text: str, hold_ms: int = 6000):
        self.q.put((who, text, hold_ms))

    def _poll(self):
        try:
            while True:
                who, text, hold = self.q.get_nowait()
                if who == "player":
                    self.player.config(text=f"Bạn: {text}" if text else "")
                    self.npc.config(text="")
                else:
                    self.npc.config(text=text)
                if self._clear_job:
                    self.root.after_cancel(self._clear_job)
                self._clear_job = self.root.after(hold, self._clear)
        except queue.Empty:
            pass
        self.root.after(100, self._poll)

    def _clear(self):
        self.player.config(text="")
        self.npc.config(text="")

    def run(self):
        self.root.mainloop()


class NoOverlay:
    def show(self, who, text, hold_ms=6000):
        print(f"[{who}] {text}")

    def run(self):
        import threading
        threading.Event().wait()
