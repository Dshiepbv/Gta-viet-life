"""Data Pack loader: schema check, fixed NPC identities (M0), dialogue selection (M2), regional prices (M4).

The pack is read-only reference data. Anything derived from it for a player (an NPC identity) is written once
to SQLite and never regenerated, so updating the pack never rewrites a save.
"""
from __future__ import annotations

import hashlib
import json
import logging
import re
import sqlite3
import threading
from pathlib import Path
from typing import Dict, List, Optional

LOG = logging.getLogger("gvl.engine")
SECTIONS = ("npc_names", "occupations", "food_drink_menu", "goods_prices", "dialogue_templates", "mlo_catalog_candidates")
RICH_ZONES = {"RICHM", "ROCKF", "BHAMCA", "CHIL", "GOLF", "PBLUFF", "RGLEN", "TONGVAH", "BANHAMC", "MORN", "WVINE"}
POOR_ZONES = {"STRAW", "DAVIS", "RANCHO", "CHAMH", "SANDY", "GRAPES", "STAD", "CYPRE", "ELYSIAN", "MURRI", "ZANCUDO",
              "HARMO", "PALETO", "EAST_V", "LMESA"}

# Words that reveal what the player wants, used to pick an appropriate template; no match means small talk.
INTENT_WORDS = (
    ("farewell", ("tạm biệt", "đi trước", "hẹn gặp lại", "về đây")),
    ("greeting", ("chào", "khỏe không", "xin chào")),
    ("food_order", ("phở", "bún", "cơm", "trà đá", "cà phê", "cho tôi một", "tính tiền", "món")),
    ("shopping", ("bao nhiêu", "giá", "mua", "rẻ hơn", "giao hàng")),
    ("repair", ("sửa", "xe tôi", "tiếng kêu", "hỏng")),
    ("transport", ("đường", "bắt xe", "đi ra", "kẹt xe")),
    ("workplace", ("báo cáo", "tăng ca", "đi làm", "sếp")),
    ("money", ("lương", "chuyển khoản", "tiền")),
    ("weather", ("mưa", "nắng", "trời")),
    ("emotional", ("mệt", "buồn", "chán", "stress")),
    ("conflict", ("không đồng ý", "sao anh lại", "tức", "cãi")),
    ("request", ("giúp", "mượn", "nhờ")),
    ("family", ("con đi học", "ăn cơm ở nhà", "mua ít rau")),
    ("neighbor", ("chuyển đến", "ồn", "hàng xóm")),
    ("relationship", ("cuối tuần", "lâu rồi không gặp", "đi chơi")),
)
# Where an intent makes sense; an intent not listed fits anywhere.
INTENT_PLACES = {"food_order": {"restaurant", "cafe", "street_food"}, "shopping": {"shop", "market", "restaurant", "cafe"},
                 "workplace": {"work", "office"}, "repair": {"garage", "work"}, "neighbor": {"home", "street"},
                 "family": {"home"}}


class DataPackError(ValueError):
    pass


def _check(value, schema: dict, where: str, errors: List[str]) -> None:
    kind = schema.get("type")
    types = {"object": dict, "array": list, "string": str, "integer": int, "number": (int, float)}
    if kind and (not isinstance(value, types[kind]) or (kind in ("integer", "number") and isinstance(value, bool))):
        errors.append("%s: phải là %s" % (where, kind))
        return
    if "enum" in schema and value not in schema["enum"]:
        errors.append("%s: giá trị %r không hợp lệ" % (where, value))
    if isinstance(value, str):
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            errors.append("%s: dài quá %d ký tự" % (where, schema["maxLength"]))
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errors.append("%s: sai định dạng" % where)
    if isinstance(value, (int, float)) and not isinstance(value, bool) and "minimum" in schema and value < schema["minimum"]:
        errors.append("%s: nhỏ hơn %s" % (where, schema["minimum"]))
    if isinstance(value, dict):
        for key in schema.get("required", ()):
            if key not in value:
                errors.append("%s: thiếu %s" % (where, key))
        for key, sub in schema.get("properties", {}).items():
            if key in value:
                _check(value[key], sub, "%s.%s" % (where, key), errors)
    if isinstance(value, list):
        if len(value) < schema.get("minItems", 0):
            errors.append("%s: cần ít nhất %d mục" % (where, schema["minItems"]))
        for index, item in enumerate(value):
            if "items" in schema:
                _check(item, schema["items"], "%s[%d]" % (where, index), errors)


def validate(sections: Dict[str, object], schema: dict) -> List[str]:
    errors: List[str] = []
    for name, data in sections.items():
        _check(data, schema[name], name, errors)
        if isinstance(data, list):
            ids = [row.get("id") for row in data if isinstance(row, dict)]
            if len(ids) != len(set(ids)):
                errors.append("%s: trùng id" % name)
            for row in data:
                if isinstance(row, dict) and isinstance(row.get("price_min"), int) and isinstance(row.get("price_max"), int) \
                        and row["price_min"] > row["price_max"]:
                    errors.append("%s.%s: price_min lớn hơn price_max" % (name, row.get("id")))
                if isinstance(row, dict) and isinstance(row.get("monthly_income_min"), int) \
                        and isinstance(row.get("monthly_income_max"), int) and row["monthly_income_min"] > row["monthly_income_max"]:
                    errors.append("%s.%s: thu nhập min lớn hơn max" % (name, row.get("id")))
    return errors


def _hash(text: str) -> int:
    return int.from_bytes(hashlib.sha256(text.encode("utf-8")).digest()[:8], "big")


def zone_wealth(zone: str) -> int:
    zone = (zone or "").upper()
    return 2 if zone in RICH_ZONES else 0 if zone in POOR_ZONES else 1


class DataPack:
    def __init__(self, folder: Path, meta: dict, sections: Dict[str, list]):
        self.folder = folder
        self.meta = meta
        self.version = meta["version"]
        self.currency = meta["currency"]
        self.names = sections["npc_names"]
        self.occupations = sections["occupations"]
        self.menu = sections["food_drink_menu"]
        self.goods = sections["goods_prices"]
        self.dialogue = sections["dialogue_templates"]
        self.mlo_candidates = sections["mlo_catalog_candidates"]
        self._items = {row["id"]: row for row in self.menu + self.goods}

    @classmethod
    def load(cls, folder, schema_path=None) -> "DataPack":
        folder = Path(folder)
        schema_path = Path(schema_path) if schema_path else folder.parent / "schema" / "datapack.schema.json"
        try:
            schema = json.loads(schema_path.read_text(encoding="utf-8"))
            meta = json.loads((folder / "pack.json").read_text(encoding="utf-8"))
            errors: List[str] = []
            _check(meta, schema["pack"], "pack", errors)
            sections = {}
            for name in SECTIONS:
                raw = (folder / (name + ".json")).read_bytes()
                listed = meta.get("files", {}).get(name + ".json", {}) if isinstance(meta.get("files"), dict) else {}
                if listed.get("sha256") and hashlib.sha256(raw).hexdigest() != listed["sha256"]:
                    errors.append("%s.json: sai SHA-256 so với pack.json" % name)
                sections[name] = json.loads(raw.decode("utf-8"))
        except (OSError, ValueError, KeyError) as exc:
            raise DataPackError("không đọc được Data Pack: %s" % exc) from exc
        errors += validate(sections, schema)
        if errors:
            raise DataPackError("; ".join(errors[:20]))
        return cls(folder, meta, sections)

    # ---------------------------------------------------------------- M4 prices
    def price(self, item_id: str, zone: str = "", day: int = 0) -> int:
        """Regional price: poor zones near the minimum, rich near the maximum, a small daily swing; whole VND hundreds."""
        row = self._items.get(item_id)
        if row is None:
            raise KeyError("price_item_not_found")
        low, high = row["price_min"], row["price_max"]
        position = (0.15, 0.5, 0.85)[zone_wealth(zone)]
        swing = ((_hash("%s/%d" % (item_id, day)) % 1001) / 1000.0 - 0.5) * 0.1   # +-5% per game day
        value = low + (high - low) * min(1.0, max(0.0, position + swing))
        return int(round(value / 100.0)) * 100 if value >= 1000 else int(round(value))

    def menu_for(self, kind: str) -> List[dict]:
        if kind == "cafe":
            return [r for r in self.menu if r["category"] in ("drink", "dessert", "snack")]
        if kind in ("restaurant", "street_food"):
            return [r for r in self.menu if r["category"] in ("food", "drink")]
        return list(self.goods)

    # ---------------------------------------------------------------- M2 dialogue
    @staticmethod
    def detect_intent(text: str) -> str:
        lowered = (text or "").lower()
        for intent, words in INTENT_WORDS:
            if any(word in lowered for word in words):
                return intent
        return "small_talk"

    def dialogue_for(self, intent: str, affinity: float = 0.0, stress: float = 0.0, place: str = "",
                     seed: str = "", limit: int = 2) -> List[dict]:
        """Templates that fit what is happening: no shop talk at home, no friendly plans with someone hostile."""
        if affinity <= -30 and intent in ("relationship", "request", "greeting"):
            intent = "conflict"
        elif stress >= 70 and intent in ("small_talk", "greeting", "weather"):
            intent = "emotional"
        allowed = INTENT_PLACES.get(intent)
        if allowed and place and place not in allowed:
            intent = "small_talk"
        rows = [r for r in self.dialogue if r["intent"] == intent]
        rows.sort(key=lambda r: _hash(seed + "/" + r["id"]))
        return rows[:limit]


class IdentityStore:
    """M0: fixed identities. Created once per persistent_id from the pack and never rolled again."""

    def __init__(self, connection: sqlite3.Connection, lock, pack: Optional[DataPack]):
        self.db = connection
        self.lock = lock or threading.RLock()
        self.pack = pack
        with self.lock, self.db:
            self.db.execute("""CREATE TABLE IF NOT EXISTS npc_identities(
                persistent_id TEXT PRIMARY KEY, name_id TEXT NOT NULL, display_name TEXT NOT NULL, gender TEXT NOT NULL,
                occupation_id TEXT NOT NULL, occupation TEXT NOT NULL, monthly_income INTEGER NOT NULL,
                pack_version TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)""")

    def identity(self, persistent_id: str) -> dict:
        if not isinstance(persistent_id, str) or not persistent_id or len(persistent_id) > 128:
            raise ValueError("invalid_persistent_id")
        with self.lock:
            row = self.db.execute("SELECT * FROM npc_identities WHERE persistent_id=?", (persistent_id,)).fetchone()
            if row:
                return dict(row)
            if self.pack is None:
                raise ValueError("data_pack_unavailable")
            name = self.pack.names[_hash("name/" + persistent_id) % len(self.pack.names)]
            job = self.pack.occupations[_hash("job/" + persistent_id) % len(self.pack.occupations)]
            low, high = job["monthly_income_min"], job["monthly_income_max"]
            income = low + (_hash("income/" + persistent_id) % (high - low + 1)) if high > low else low
            income = int(round(income / 100000.0)) * 100000 if income >= 100000 else income
            record = {"persistent_id": persistent_id, "name_id": name["id"],
                      "display_name": name["last_name"] + " " + name["first_name"], "gender": name["gender"],
                      "occupation_id": job["id"], "occupation": job["name"], "monthly_income": income,
                      "pack_version": self.pack.version}
            with self.db:
                self.db.execute("""INSERT OR IGNORE INTO npc_identities(persistent_id,name_id,display_name,gender,
                    occupation_id,occupation,monthly_income,pack_version) VALUES(?,?,?,?,?,?,?,?)""",
                    tuple(record[k] for k in ("persistent_id", "name_id", "display_name", "gender", "occupation_id",
                                              "occupation", "monthly_income", "pack_version")))
            return dict(self.db.execute("SELECT * FROM npc_identities WHERE persistent_id=?", (persistent_id,)).fetchone())


def default_pack_folder() -> Path:
    import sys
    bundled = Path(getattr(sys, "_MEIPASS", "")) / "data" / "packs" / "vi-VN"
    if getattr(sys, "_MEIPASS", None) and bundled.is_dir():
        return bundled
    return Path(__file__).resolve().parents[1] / "data" / "packs" / "vi-VN"


def load_default() -> Optional[DataPack]:
    """A broken pack is logged and ignored: the simulation and saves keep working without it."""
    try:
        return DataPack.load(default_pack_folder())
    except DataPackError as exc:
        LOG.error("Data Pack bị bỏ qua: %s", exc)
        return None
