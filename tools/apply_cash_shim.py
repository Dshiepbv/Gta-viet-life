#!/usr/bin/env python3
"""Doi moi script GTA Viet Life dung vi tien chung cua Core thay vi Game.Player.Money.

Nhan vat tuy bien (freemode) khong co tien trong game (Game.Player.Money luon 0). Core giu "vi" rieng va
cong bo GVL.CashGet / GVL.CashSet; moi script goi thuoc tinh PlayerCash (tu quay ve Game.Player.Money neu khong co Core).

Chay (idempotent, chay lai sau moi lan gop main neu bi xung dot):
    python3 tools/apply_cash_shim.py game/*.cs
"""
import re
import sys

SHIM = '''    // tien: nhan vat tuy bien khong co tien trong game => dung vi cua Core (GVL.CashGet/CashSet), xem docs/CORE.md
    static int PlayerCash
    {
        get { Func<int> g = AppDomain.CurrentDomain.GetData("GVL.CashGet") as Func<int>; return g != null ? g() : Game.Player.Money; }
        set { Action<int> s = AppDomain.CurrentDomain.GetData("GVL.CashSet") as Action<int>; if (s != null) s(value); else Game.Player.Money = value; }
    }

'''
MARK = "static int PlayerCash"


def patch(path):
    s = open(path, encoding="utf-8").read()
    if MARK not in s:
        m = re.search(r"public class \w+ : Script\s*\n\{\n", s)
        if not m:
            print("bo qua (khong thay class Script):", path)
            return
        s = s[:m.end()] + SHIM + s[m.end():]
    head, shim_end = s.split(MARK, 1)
    # giu nguyen than shim (chua Game.Player.Money that), chi doi phan con lai
    i = shim_end.index("\n    }\n") + len("\n    }\n")
    body = shim_end[i:]
    n = body.count("Game.Player.Money") + head.count("Game.Player.Money")
    def sub(t):   # dong co "RAW_MONEY" giu nguyen (Core doc/ghi tien that cua game)
        return "\n".join(l if "RAW_MONEY" in l else l.replace("Game.Player.Money", "PlayerCash") for l in t.split("\n"))
    head = sub(head)
    body = sub(body)
    open(path, "w", encoding="utf-8").write(head + MARK + shim_end[:i] + body)
    print(f"{path}: {n} cho doi")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        patch(p)
