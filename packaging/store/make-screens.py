#!/usr/bin/env python3
"""Store のスクリーンショットに写す、見本の業務画面（架空の経費精算アプリ）を描く。

    python3 packaging/store/make-screens.py   # rsvg-convert と Noto Sans CJK JP が必要

できた screens/*.png を、テスト（tests/Tejun.Tests/StoreShots.cs）が記録済みの画面として読み込む。
クリック位置を変えたら、StoreShots.cs の座標も合わせる。
"""
import pathlib, subprocess

W, H = 1920, 1080
FONT = "Noto Sans CJK JP"
OUT = pathlib.Path(__file__).parent / "screens"

ROWS = [
    ("2026/11/04", "交通費", "客先訪問（電車）", "1,280 円", "承認済み"),
    ("2026/11/02", "消耗品", "プリンター用紙", "2,480 円", "承認済み"),
    ("2026/10/28", "会議費", "打ち合わせ（会議室）", "3,300 円", "承認済み"),
    ("2026/10/21", "交通費", "研修会場まで（バス）", "460 円", "承認済み"),
    ("2026/10/15", "書籍", "技術書 1 冊", "3,520 円", "差し戻し"),
]
NEW_ROW = ("2026/11/10", "交通費", "展示会の見学（電車）", "1,640 円", "申請中")


def text(x, y, s, size=18, fill="#1f2430", weight="400", anchor="start"):
    return (f'<text x="{x}" y="{y}" font-family="{FONT}" font-size="{size}" fill="{fill}" '
            f'font-weight="{weight}" text-anchor="{anchor}">{s}</text>')


def desktop():
    p = [f'<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">'
         f'<stop offset="0" stop-color="#dfe9f5"/><stop offset="1" stop-color="#b9cfe8"/></linearGradient>'
         f'<filter id="sh" x="-5%" y="-5%" width="110%" height="115%"><feDropShadow dx="0" dy="8" stdDeviation="14" flood-opacity="0.22"/></filter></defs>',
         f'<rect width="{W}" height="{H}" fill="url(#bg)"/>',
         f'<rect y="{H-48}" width="{W}" height="48" fill="#232a36"/>']
    for i, c in enumerate(["#5b8def", "#8a94a6", "#8a94a6", "#e0574f"]):
        p.append(f'<rect x="{20+i*52}" y="{H-37}" width="26" height="26" rx="6" fill="{c}"/>')
    p.append(f'<rect x="170" y="{H-6}" width="38" height="3" rx="1.5" fill="#e0574f"/>')
    p.append(text(W - 24, H - 18, "10:24", 15, "#e8ecf2", anchor="end"))
    return p


def window(rows):
    x, y, w, h = 260, 110, 1400, 820
    p = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="#ffffff" filter="url(#sh)"/>',
         f'<path d="M{x},{y+44} v-34 a10,10 0 0 1 10,-10 h{w-20} a10,10 0 0 1 10,10 v34 z" fill="#f3f5f8"/>',
         f'<rect x="{x+16}" y="{y+13}" width="18" height="18" rx="4" fill="#e0574f"/>',
         text(x + 44, y + 29, "経費精算（サンプル）", 15, "#3a4150"),
         text(x + w - 28, y + 29, "―　☐　✕", 14, "#5a6272", anchor="end"),
         # ツール バー
         f'<rect x="{x+30}" y="{y+58}" width="150" height="40" rx="6" fill="#2f6fed"/>',
         text(x + 105, y + 84, "新規申請", 17, "#ffffff", "700", "middle"),
         f'<rect x="{x+196}" y="{y+58}" width="96" height="40" rx="6" fill="#ffffff" stroke="#c9d0db"/>',
         text(x + 244, y + 84, "更新", 17, "#3a4150", anchor="middle"),
         f'<rect x="{x+w-330}" y="{y+58}" width="300" height="40" rx="6" fill="#ffffff" stroke="#c9d0db"/>',
         text(x + w - 314, y + 84, "内容で検索", 16, "#9aa3b2"),
         # 表
         f'<rect x="{x+30}" y="{y+124}" width="{w-60}" height="46" fill="#f3f5f8"/>']
    cols = [(x + 54, "日付"), (x + 254, "区分"), (x + 434, "内容"), (x + 1010, "金額"), (x + 1200, "状態")]
    for cx, name in cols:
        p.append(text(cx, y + 153, name, 15, "#5a6272", "700"))
    for i, row in enumerate(rows):
        ry = y + 170 + i * 52
        p.append(f'<rect x="{x+30}" y="{ry+51}" width="{w-60}" height="1" fill="#e6e9ef"/>')
        for (cx, _), cell in zip(cols, row):
            if cell in ("承認済み", "申請中", "差し戻し"):
                bg, fg = {"承認済み": ("#e5f4ea", "#23743c"), "申請中": ("#e6eefc", "#2457c5"), "差し戻し": ("#fdeceb", "#b3372f")}[cell]
                p.append(f'<rect x="{cx-10}" y="{ry+12}" width="96" height="28" rx="14" fill="{bg}"/>')
                p.append(text(cx + 38, ry + 32, cell, 14, fg, "700", "middle"))
            else:
                p.append(text(cx, ry + 33, cell, 17))
    p.append(text(x + 30, y + h - 26, f"全 {len(rows)} 件", 14, "#7a8394"))
    return p


def dialog():
    x, y, w, h = 600, 280, 720, 520
    p = [f'<rect x="260" y="110" width="1400" height="820" rx="10" fill="#1f2430" opacity="0.35"/>',
         f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="#ffffff" filter="url(#sh)"/>',
         text(x + 36, y + 56, "新規申請", 24, weight="700")]
    fields = [("日付", NEW_ROW[0]), ("区分", NEW_ROW[1]), ("内容", NEW_ROW[2]), ("金額", NEW_ROW[3])]
    for i, (label, value) in enumerate(fields):
        fy = y + 96 + i * 78
        p.append(text(x + 36, fy + 16, label, 14, "#5a6272", "700"))
        p.append(f'<rect x="{x+36}" y="{fy+26}" width="{w-72}" height="40" rx="6" fill="#ffffff" stroke="#c9d0db"/>')
        p.append(text(x + 50, fy + 53, value, 17))
    p += [f'<rect x="{x+w-326}" y="{y+h-70}" width="130" height="44" rx="6" fill="#ffffff" stroke="#c9d0db"/>',
          text(x + w - 261, y + h - 41, "キャンセル", 17, "#3a4150", anchor="middle"),
          f'<rect x="{x+w-176}" y="{y+h-70}" width="140" height="44" rx="6" fill="#2f6fed"/>',
          text(x + w - 106, y + h - 41, "申請する", 17, "#ffffff", "700", "middle")]
    return p


SCREENS = {
    "screen-1.png": desktop() + window(ROWS),                 # 「新規申請」をクリック (365, 188)
    "screen-2.png": desktop() + window(ROWS) + dialog(),      # 「申請する」をクリック (1214, 752)
    "screen-3.png": desktop() + window([NEW_ROW] + ROWS),     # 新しい行をダブルクリック (960, 306)
}

OUT.mkdir(exist_ok=True)
for name, parts in SCREENS.items():
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">' + "".join(parts) + "</svg>"
    subprocess.run(["rsvg-convert", "-o", str(OUT / name)], input=svg.encode(), check=True)
    print(OUT / name)
