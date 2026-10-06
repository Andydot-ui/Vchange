# -*- coding: utf-8 -*-
"""
Vchange Apple-style promo video renderer.

Renders a 1920x1080 / 30fps cinematic promo (black background, gradient
accents, staggered reveals, cross-fades) with Pillow, pipes raw frames into
the bundled ffmpeg (libx264), generates an ambient music bed with numpy,
then muxes everything into one MP4.

Usage:
    python make-promo.py [--out Vchange-promo-1080p.mp4] [--preview t1,t2,...]

Requires: Pillow, numpy, Resources\\ffmpeg.exe
"""
import argparse
import math
import os
import subprocess
import sys
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# ----------------------------------------------------------------------------
# Constants
# ----------------------------------------------------------------------------
W, H = 1920, 1080
FPS = 30
TRANS = 0.9  # cross-fade duration between scenes (s)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FFMPEG = os.path.join(ROOT, "Resources", "ffmpeg.exe")
SHOTS = os.path.join(ROOT, "docs", "screenshots")
LOGO = os.path.join(ROOT, "docs", "images", "logo-dark-theme.png")

F_YAHEI_B = "C:/Windows/Fonts/msyhbd.ttc"
F_YAHEI = "C:/Windows/Fonts/msyh.ttc"
F_SEGOE_B = "C:/Windows/Fonts/segoeuib.ttf"
F_SEGOE_SB = "C:/Windows/Fonts/seguisb.ttf"

WHITE = (245, 245, 247, 255)
SEC = (161, 161, 166, 255)
TER = (134, 134, 139, 255)
# Apple-ish gradient: blue -> purple -> pink
GRAD = [(0.0, (0, 113, 227)), (0.55, (162, 89, 255)), (1.0, (255, 95, 109))]

_font_cache = {}


def font(path, size):
    key = (path, size)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(path, size)
    return _font_cache[key]


def ease_out(p):
    p = max(0.0, min(1.0, p))
    return 1 - (1 - p) ** 3


def ease_in_out(p):
    p = max(0.0, min(1.0, p))
    return 4 * p ** 3 if p < 0.5 else 1 - (-2 * p + 2) ** 3 / 2


def grad_array(w, h, stops=GRAD, diagonal=False):
    """Horizontal (or diagonal) RGB gradient as uint8 array (h, w, 3)."""
    if w <= 0 or h <= 0:
        return np.zeros((max(h, 1), max(w, 1), 3), np.uint8)
    if diagonal:
        xs = np.linspace(0, 1, w)[None, :]
        ys = np.linspace(0, 1, h)[:, None]
        t = np.clip((xs + ys) * 0.5, 0, 1)
    else:
        t = np.linspace(0, 1, w)[None, :].repeat(h, axis=0)
    pos = np.array([s[0] for s in stops])
    cols = np.array([s[1] for s in stops], dtype=np.float64)
    out = np.empty(t.shape + (3,), np.float64)
    for c in range(3):
        out[..., c] = np.interp(t, pos, cols[:, c])
    return out.astype(np.uint8)


def _measure(fnt, text, tracking):
    dummy = ImageDraw.Draw(Image.new("L", (4, 4)))
    widths = [dummy.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + tracking * (len(text) - 1) if text else 0
    return total, widths


def text_patch(text, fnt, fill=WHITE, tracking=0.0, gradient=None,
               stroke=None):
    """Render text to an RGBA patch; text centered inside the patch."""
    if not text:
        return Image.new("RGBA", (4, 4), (0, 0, 0, 0))
    total, widths = _measure(fnt, text, tracking)
    pad = 10
    ph = int(fnt.size * 1.7)
    pw = int(math.ceil(total)) + pad * 2
    if gradient is not None:
        arr = grad_array(pw, ph, gradient)
        patch = Image.fromarray(arr, "RGB").convert("RGBA")
        mask = Image.new("L", (pw, ph), 0)
        md = ImageDraw.Draw(mask)
        x = float(pad)
        for ch, cw in zip(text, widths):
            md.text((x, ph / 2), ch, font=fnt, fill=255, anchor="lm")
            x += cw + tracking
        patch.putalpha(mask)
    else:
        patch = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
        d = ImageDraw.Draw(patch)
        x = float(pad)
        for ch, cw in zip(text, widths):
            d.text((x, ph / 2), ch, font=fnt, fill=fill, anchor="lm",
                   stroke_width=stroke and 1 or 0, stroke_fill=stroke)
            x += cw + tracking
    return patch


def make_bg(glow=None, intensity=0.22, rx=1100, ry=700):
    """Pure black canvas with an optional soft radial glow (RGBA)."""
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    if glow is None:
        return canvas
    ys, xs = np.mgrid[0:H, 0:W]
    cx, cy = glow[0], glow[1]
    d = np.sqrt(((xs - cx) / rx) ** 2 + ((ys - cy) / ry) ** 2)
    k = np.clip(1 - d, 0, 1) ** 2.2 * intensity
    arr = np.zeros((H, W, 3), np.float64)
    col = np.array(glow[2], dtype=np.float64)
    arr[:] = col
    arr = (arr * k[..., None]).astype(np.uint8)
    glow_img = Image.fromarray(arr, "RGB").convert("RGBA")
    return Image.alpha_composite(canvas, glow_img.filter(ImageFilter.GaussianBlur(6)))


def rounded_shadow(w, h, radius=18, blur=34, offset=(0, 24), spread=70):
    """RGBA patch of a blurred rounded-rect shadow with margin `spread`."""
    pad = spread
    img = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=radius,
                        fill=(0, 0, 0, 210))
    img = img.filter(ImageFilter.GaussianBlur(blur))
    if offset != (0, 0):
        img = ImageChops_offset(img, offset)
    return img


def ImageChops_offset(img, off):
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.paste(img, off)
    return out


def icon_patch(kind, size=96, ss=3):
    """Hand-drawn accent icon on a tinted rounded square (supersampled)."""
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=int(s * 0.26),
                        fill=(41, 151, 255, 34), outline=(41, 151, 255, 90),
                        width=ss)
    c = (41, 151, 255, 255)
    lw = max(3, int(s * 0.045))
    m = int(s * 0.24)          # margin
    if kind == "video":
        d.rounded_rectangle((m, int(s * 0.30), s - m, int(s * 0.70)),
                            radius=int(s * 0.07), outline=c, width=lw)
        tri = [((s - m) - int(s * 0.20), int(s * 0.38)),
               ((s - m) - int(s * 0.20), int(s * 0.62)),
               ((s - m) - int(s * 0.05), int(s * 0.50))]
        d.polygon(tri, fill=c)
    elif kind == "clock":
        cc = s // 2
        r = int(s * 0.26)
        d.ellipse((cc - r, cc - r, cc + r, cc + r), outline=c, width=lw)
        d.line((cc, cc, cc, cc - int(r * 0.58)), fill=c, width=lw)
        d.line((cc, cc, cc + int(r * 0.50), cc + int(r * 0.22)), fill=c,
               width=lw)
    elif kind == "stack":
        for i, off in enumerate((0.16, 0.0, -0.16)):
            y = int(s * (0.50 + off))
            x0 = int(s * (0.24 + i * 0.03))
            x1 = s - int(s * (0.24 + i * 0.03))
            d.rounded_rectangle((x0, y - int(s * 0.10), x1, y + int(s * 0.10)),
                                radius=int(s * 0.05), outline=c, width=lw)
    else:  # image
        d.rounded_rectangle((m, m, s - m, s - m), radius=int(s * 0.07),
                            outline=c, width=lw)
        r = int(s * 0.07)
        sx, sy = int(s * 0.36), int(s * 0.36)
        d.ellipse((sx - r, sy - r, sx + r, sy + r), outline=c, width=lw)
        d.line((m + int(s * 0.10), s - m - int(s * 0.12),
                int(s * 0.47), int(s * 0.55),
                s - m - int(s * 0.10), s - m - int(s * 0.20)), fill=c,
               width=lw, joint="curve")
    return img.resize((size, size), Image.LANCZOS)


def window_card(screenshot_path, target_h):
    """Screenshot inside a hairline rounded card with a soft drop shadow."""
    img = Image.open(screenshot_path).convert("RGB")
    scale = target_h / img.height
    img = img.resize((int(img.width * scale), target_h), Image.LANCZOS)
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, img.width - 1, img.height - 1),
                                           radius=16, fill=255)
    card = img.convert("RGBA")
    card.putalpha(mask)

    pad = 70
    canvas = Image.new("RGBA", (img.width + pad * 2, img.height + pad * 2),
                       (0, 0, 0, 0))
    shadow = rounded_shadow(img.width, img.height, radius=16, blur=36,
                            offset=(0, 26), spread=pad)
    canvas.alpha_composite(shadow)
    canvas.alpha_composite(card, (pad, pad))
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((pad - 1, pad - 1, pad + img.width, pad + img.height),
                        radius=17, outline=(255, 255, 255, 46), width=2)
    return canvas


# ----------------------------------------------------------------------------
# 3D perspective helpers (PIL homography warps)
# ----------------------------------------------------------------------------
def _persp_coeffs(dst_quad, src_quad):
    """Coeffs for PIL PERSPECTIVE: map each dest pixel back into src coords."""
    A, B = [], []
    for (x, y), (xs, ys) in zip(dst_quad, src_quad):
        A.append([x, y, 1, 0, 0, 0, -x * xs, -y * xs])
        B.append(xs)
        A.append([0, 0, 0, x, y, 1, -x * ys, -y * ys])
        B.append(ys)
    return tuple(np.linalg.solve(np.asarray(A, float), np.asarray(B, float)))


def warp_y(patch, deg):
    """Yaw: perspective rotation around the vertical axis (card 3D swing)."""
    if abs(deg) < 0.15:
        return patch
    w, h = patch.size
    th = math.radians(deg)
    cx, cy = w / 2.0, h / 2.0
    d = 3.0 * w

    def proj(px, py):
        x, y = px - cx, py - cy
        X = x * math.cos(th)
        Z = x * math.sin(th)
        k = d / (d - Z)
        return cx + X * k, cy + y * k

    srcq = [(0, 0), (w, 0), (w, h), (0, h)]
    dstq = [proj(*p) for p in srcq]
    xs = [p[0] for p in dstq]
    ys = [p[1] for p in dstq]
    minx, miny = min(xs), min(ys)
    Wc = int(math.ceil(max(xs) - minx)) + 4
    Hc = int(math.ceil(max(ys) - miny)) + 4
    off = (-minx + 2.0, -miny + 2.0)
    dst_shift = [(p[0] + off[0], p[1] + off[1]) for p in dstq]
    return patch.transform((Wc, Hc), Image.PERSPECTIVE,
                           _persp_coeffs(dst_shift, srcq), Image.BICUBIC)


def warp_x(patch, deg):
    """Pitch: perspective rotation around the horizontal axis (text 3D pop)."""
    if abs(deg) < 0.15:
        return patch
    w, h = patch.size
    th = math.radians(deg)
    cx, cy = w / 2.0, h / 2.0
    d = 3.0 * h

    def proj(px, py):
        x, y = px - cx, py - cy
        Y = y * math.cos(th)
        Z = y * math.sin(th)
        k = d / (d - Z)
        return cx + x * k, cy + Y * k

    srcq = [(0, 0), (w, 0), (w, h), (0, h)]
    dstq = [proj(*p) for p in srcq]
    xs = [p[0] for p in dstq]
    ys = [p[1] for p in dstq]
    minx, miny = min(xs), min(ys)
    Wc = int(math.ceil(max(xs) - minx)) + 4
    Hc = int(math.ceil(max(ys) - miny)) + 4
    off = (-minx + 2.0, -miny + 2.0)
    dst_shift = [(p[0] + off[0], p[1] + off[1]) for p in dstq]
    return patch.transform((Wc, Hc), Image.PERSPECTIVE,
                           _persp_coeffs(dst_shift, srcq), Image.BICUBIC)


def anim3d(patch, axis, t0, enter_dur, th_in, idle=0.0, period=7.0,
           phase=0.0, t1=None, th_exit=0.0):
    """Patch callable for Layer: 3D swing-in, continuous idle float, exit twist.

    axis='y' yaw (cards), axis='x' pitch (headlines).  Enter is aligned with
    the layer's fade-in, which build_timeline shifts by TRANS * 0.5.
    """
    warp = warp_y if axis == "y" else warp_x
    shift = TRANS * 0.5

    def draw(t):
        th = 0.0
        if idle:
            th += idle * math.sin(2 * math.pi * t / period + phase)
        if t < t0 + shift + enter_dur:
            p = ease_out((t - shift - t0) / enter_dur)
            th += th_in * (1.0 - p)
        if t1 is not None and t > t1:
            th += th_exit * ease_in_out(min(1.0, (t - t1) / 0.7))
        return warp(patch, th)
    return draw


# ----------------------------------------------------------------------------
# Layer / Scene
# ----------------------------------------------------------------------------
class Layer:
    """A patch that fades/slides/zooms in and out at given local times."""

    def __init__(self, cx, cy, patch, t0, dur=0.75, t1=None, exit_dur=0.55,
                 rise=26, zoom_from=1.0, fade_out=True):
        self.cx, self.cy = cx, cy
        self.patch = patch            # RGBA image or callable(t)->RGBA
        self.t0, self.dur = t0, dur
        self.t1 = t1
        self.exit_dur = exit_dur
        self.rise = rise
        self.zoom_from = zoom_from
        self.fade_out = fade_out

    def composite(self, base, t):
        if t < self.t0:
            return
        patch = self.patch(t) if callable(self.patch) else self.patch
        if patch is None:
            return
        e = ease_out((t - self.t0) / self.dur)
        alpha = e
        dy = self.rise * (1 - e)
        scale = self.zoom_from + (1 - self.zoom_from) * e
        if self.t1 is not None and t > self.t1:
            p = (t - self.t1) / self.exit_dur
            if p >= 1:
                return
            alpha *= 1 - ease_in_out(p)
            scale += 0.03 * p
            dy -= 24 * p
        if alpha <= 0.004:
            return
        w, h = patch.size
        if abs(scale - 1.0) > 0.001:
            nw, nh = max(2, int(w * scale)), max(2, int(h * scale))
            patch = patch.resize((nw, nh), Image.BILINEAR)
            w, h = nw, nh
        if alpha < 0.997:
            lut = [int(i * alpha) for i in range(256)]
            patch = patch.copy()
            patch.putalpha(patch.getchannel("A").point(lut))
        x = int(self.cx - w / 2)
        y = int(self.cy - h / 2 + dy)
        base.alpha_composite(patch, (x, y))


class Scene:
    def __init__(self, bg, layers):
        self.bg = bg
        self.layers = layers

    def frame(self, t):
        base = self.bg.copy()
        for lay in self.layers:
            lay.composite(base, t)
        return base


def centered_row(title, desc, icon):
    """Icon + two-line text block, returned as one patch (top-left origin)."""
    f_title = font(F_YAHEI_B, 44)
    f_desc = font(F_YAHEI, 29)
    gap = 34
    tp = text_patch(title, f_title, WHITE, tracking=1.5)
    dp = text_patch(desc, f_desc, SEC, tracking=0.8)
    tw = max(tp.width, dp.width)
    icon_size = 92
    row_w = icon_size + gap + tw
    row_h = 96
    img = Image.new("RGBA", (row_w, row_h), (0, 0, 0, 0))
    img.alpha_composite(icon, (0, (row_h - icon_size) // 2))
    x = icon_size + gap
    img.alpha_composite(tp, (x, 4))
    img.alpha_composite(dp, (x, 52))
    return img


# ----------------------------------------------------------------------------
# Scenes
# ----------------------------------------------------------------------------
def scene_logo():
    bg = make_bg(glow=(960, 470, (10, 132, 255)), intensity=0.20)
    logo = Image.open(LOGO).convert("RGBA").resize((216, 216), Image.LANCZOS)
    layers = [
        Layer(960, 400, anim3d(logo, "y", 0.30, 1.4, 11, idle=2.6,
                               period=7.0, phase=0.5),
              0.30, dur=1.4, zoom_from=1.12, rise=0),
        Layer(960, 610,
              anim3d(text_patch("Vchange", font(F_SEGOE_SB, 96), WHITE,
                                tracking=3), "x", 1.60, 1.0, -8,
                     idle=1.5, period=9.0, phase=1.0),
              1.60, dur=1.0, rise=22),
        Layer(960, 715, text_patch("Windows 多媒体转换工具", font(F_YAHEI, 34),
                                   SEC, tracking=8), 3.00, dur=1.0, rise=18),
    ]
    return Scene(bg, layers)


def scene_tagline():
    bg = make_bg(glow=(960, 380, (162, 89, 255)), intensity=0.18)
    layers = [
        Layer(960, 470,
              anim3d(text_patch("转换，轻而易举。", font(F_YAHEI_B, 118),
                                WHITE, tracking=6), "x", 0.50, 1.1, -8,
                     idle=1.8, period=9.0, phase=0.7),
              0.50, dur=1.1, zoom_from=1.05),
        Layer(960, 655, text_patch("视频 · 图片 · 延时 · 堆砌，四合一",
                                   font(F_YAHEI_B, 46),
                                   gradient=GRAD, tracking=4), 2.20,
              dur=1.0, rise=20),
    ]
    return Scene(bg, layers)


def scene_tools():
    bg = make_bg(glow=(960, 980, (10, 132, 255)), intensity=0.14)
    rows = [
        ("视频转换", "21 种格式 · 硬件加速 · HDR 色彩空间", "video"),
        ("延时合成", "自动补零重命名 · 双进度条 · 合成后自动播放", "clock"),
        ("图片堆砌", "最大值 · 平均值 · 最小值 · 16-bit DNG", "stack"),
        ("图片转换", "JPG · PNG · WebP · DNG · SDR / HDR 色彩空间", "image"),
    ]
    layers = [
        Layer(960, 195,
              anim3d(text_patch("四个工具，一个 exe。", font(F_YAHEI_B, 64),
                                WHITE, tracking=4), "x", 0.40, 0.9, -7,
                     idle=1.3, period=10.0, phase=1.4),
              0.40, dur=0.9),
    ]
    ys = [395, 555, 715, 875]
    for i, (title, desc, kind) in enumerate(rows):
        patch = centered_row(title, desc, icon_patch(kind))
        layers.append(Layer(960, ys[i], patch, 2.0 + i * 1.9, dur=0.85,
                            rise=30, zoom_from=1.02))
    return Scene(bg, layers)


def scene_stats():
    bg = make_bg(glow=(960, 540, (162, 89, 255)), intensity=0.16)
    f_num = font(F_SEGOE_B, 176)
    f_lab = font(F_YAHEI, 34)
    items = [(460, "4", "大核心功能", 0.80), (960, "21", "种视频格式", 1.60),
             (1460, "0", "项环境配置", 2.40)]

    def make_counter(cx, target, t0):
        target = int(target)

        def draw(t):
            p = ease_out((t - t0) / 1.5)
            if p <= 0:
                return None
            val = int(round(target * p))
            patch = text_patch(str(val), f_num, gradient=GRAD, tracking=-4)
            # 3D: swing upright while counting, then keep a gentle float
            th = 7.0 * (1.0 - p) + 1.7 * math.sin(
                2 * math.pi * t / 6.0 + cx / 320.0)
            return warp_x(patch, th)
        return draw

    layers = [
        Layer(960, 250,
              anim3d(text_patch("样样俱到。", font(F_YAHEI_B, 60), WHITE,
                                tracking=4), "x", 0.40, 0.9, -7,
                     idle=1.3, period=9.0, phase=0.4),
              0.40, dur=0.9),
    ]
    for cx, target, label, t0 in items:
        layers.append(Layer(cx, 500, make_counter(cx, target, t0), t0,
                            dur=0.3, rise=0, fade_out=False))
        layers.append(Layer(cx, 665, text_patch(label, f_lab, SEC, tracking=6),
                            t0 + 0.2, dur=0.7, rise=14))
    return Scene(bg, layers)


def scene_home():
    """The four-entry home screen (real screenshot)."""
    bg = make_bg(glow=(960, 520, (10, 132, 255)), intensity=0.13)
    f_cap = font(F_YAHEI, 38)
    card = window_card(os.path.join(SHOTS, "\u5c4f\u5e55\u622a\u56fe 2026-10-06 125006.png"), 760)
    layers = [
        Layer(960, 510,
              anim3d(card, "x", 1.0, 0.8, 9, idle=2.2, period=6.5,
                     phase=0.4, t1=6.6, th_exit=-5),
              1.0, dur=0.8, t1=6.6, exit_dur=0.7, zoom_from=0.965, rise=16),
        Layer(960, 1010, text_patch("首页四选一 · 视频 / 延时 / 堆砌 / 图片",
                                    f_cap, SEC, tracking=3),
              1.5, dur=0.7, t1=6.6, exit_dur=0.6, rise=14),
    ]
    return Scene(bg, layers)


def scene_gallery():
    bg = make_bg(glow=(960, 520, (10, 132, 255)), intensity=0.13)
    shots = [
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125107.png",
         "视频参数 · 色彩空间与 HDR", 1.0),
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125045.png",
         "21 种输出格式，一目了然", 5.4),
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125129.png",
         "实时进度与原始日志", 9.8),
    ]
    layers = []
    f_cap = font(F_YAHEI, 38)
    for i, (fname, caption, t0) in enumerate(shots):
        card = window_card(os.path.join(SHOTS, fname), 760)
        animated = anim3d(
            card, "y", t0, 0.8, 13 if i % 2 == 0 else -13,
            idle=2.6, period=7.0, phase=i * 2.1,
            t1=min(t0 + 3.5, 13.5 - TRANS),
            th_exit=-7 if i % 2 == 0 else 7)
        layers.append(Layer(960, 510, animated, t0, dur=0.8, t1=t0 + 3.5,
                            exit_dur=0.7, zoom_from=0.965, rise=16))
        layers.append(Layer(960, 1010, text_patch(caption, f_cap, SEC,
                                                  tracking=3),
                            t0 + 0.5, dur=0.7, t1=t0 + 3.5, exit_dur=0.6,
                            rise=14))
    return Scene(bg, layers)


def scene_gallery2():
    """Screenshots of the three new tools."""
    bg = make_bg(glow=(960, 520, (162, 89, 255)), intensity=0.13)
    shots = [
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125209.png",
         "延时合成 · 复制 / 就地重命名方案", 1.0),
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125320.png",
         "图片堆砌 · 最大值 / 平均 / 最小值", 5.4),
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125402.png",
         "图片转换 · 7 种输出格式", 9.8),
    ]
    layers = []
    f_cap = font(F_YAHEI, 38)
    for i, (fname, caption, t0) in enumerate(shots):
        card = window_card(os.path.join(SHOTS, fname), 760)
        animated = anim3d(
            card, "y", t0, 0.8, 13 if i % 2 == 0 else -13,
            idle=2.6, period=7.0, phase=i * 2.1,
            t1=min(t0 + 3.5, 13.5 - TRANS),
            th_exit=-7 if i % 2 == 0 else 7)
        layers.append(Layer(960, 510, animated, t0, dur=0.8, t1=t0 + 3.5,
                            exit_dur=0.7, zoom_from=0.965, rise=16))
        layers.append(Layer(960, 1010, text_patch(caption, f_cap, SEC,
                                                  tracking=3),
                            t0 + 0.5, dur=0.7, t1=t0 + 3.5, exit_dur=0.6,
                            rise=14))
    return Scene(bg, layers)


def scene_gallery3():
    """Progress bars and options in action (real captures)."""
    bg = make_bg(glow=(960, 520, (10, 132, 255)), intensity=0.13)
    shots = [
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125302.png",
         "延时合成 · 双进度条实时反馈", 1.0),
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125340.png",
         "图片堆砌 · 逐张进度可见", 5.4),
        ("\u5c4f\u5e55\u622a\u56fe 2026-10-06 125412.png",
         "输出色彩空间 · SDR / HDR 全选项", 9.8),
    ]
    layers = []
    f_cap = font(F_YAHEI, 38)
    for i, (fname, caption, t0) in enumerate(shots):
        card = window_card(os.path.join(SHOTS, fname), 760)
        animated = anim3d(
            card, "y", t0, 0.8, 13 if i % 2 == 0 else -13,
            idle=2.6, period=7.0, phase=i * 2.1,
            t1=min(t0 + 3.5, 13.5 - TRANS),
            th_exit=-7 if i % 2 == 0 else 7)
        layers.append(Layer(960, 510, animated, t0, dur=0.8, t1=t0 + 3.5,
                            exit_dur=0.7, zoom_from=0.965, rise=16))
        layers.append(Layer(960, 1010, text_patch(caption, f_cap, SEC,
                                                  tracking=3),
                            t0 + 0.5, dur=0.7, t1=t0 + 3.5, exit_dur=0.6,
                            rise=14))
    return Scene(bg, layers)


def scene_highlights():
    bg = make_bg(glow=(960, 900, (255, 95, 109)), intensity=0.12)
    lines = [
        "SDR / HDR 色彩空间，Rec.2100 PQ · HLG",
        "NVENC · Quick Sync · AMF 硬件加速",
        "16-bit 线性 DNG · 无 EXIF 纯净输出",
    ]
    layers = [
        Layer(960, 250,
              anim3d(text_patch("专业，藏在细节里。", font(F_YAHEI_B, 60),
                                WHITE, tracking=4), "x", 0.40, 0.9, -7,
                     idle=1.2, period=9.5, phase=1.9),
              0.40, dur=0.9),
    ]
    for i, line in enumerate(lines):
        layers.append(Layer(
            960, 450 + i * 150,
            anim3d(text_patch(line, font(F_YAHEI_B, 50), gradient=GRAD,
                              tracking=2), "x", 2.0 + i * 1.5, 0.9, -6,
                   idle=1.1, period=8.0, phase=i * 1.7),
            2.0 + i * 1.5, dur=0.9, rise=24))
    return Scene(bg, layers)


def scene_outbox():
    bg = make_bg(glow=(960, 470, (10, 132, 255)), intensity=0.18)
    layers = [
        Layer(960, 440,
              anim3d(text_patch("下载即用。", font(F_YAHEI_B, 120), WHITE,
                                tracking=8), "x", 0.50, 1.1, -9,
                     idle=1.8, period=9.0, phase=0.2),
              0.50, dur=1.1, zoom_from=1.05),
        Layer(960, 655, text_patch("单文件绿色版 · 内嵌完整 ffmpeg 与 .NET 8",
                                   font(F_YAHEI, 42), SEC, tracking=3),
              2.30, dur=0.9, rise=18),
        Layer(960, 755, text_patch("0 项环境配置 · GPL-3.0 开源免费",
                                   font(F_YAHEI, 42), SEC, tracking=3),
              3.40, dur=0.9, rise=18),
    ]
    return Scene(bg, layers)


def scene_end():
    bg = make_bg(glow=(960, 560, (162, 89, 255)), intensity=0.20)
    logo = Image.open(LOGO).convert("RGBA").resize((150, 150), Image.LANCZOS)

    # gradient pill CTA
    pw, ph, r = 420, 104, 52
    pad = 34
    pill = Image.new("RGBA", (pw + pad * 2, ph + pad * 2), (0, 0, 0, 0))
    grad = Image.fromarray(grad_array(pw, ph), "RGB").convert("RGBA")
    mask = Image.new("L", (pw, ph), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, pw - 1, ph - 1), radius=r,
                                           fill=255)
    g = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
    g.paste(grad, (0, 0), mask)
    glow = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
    glow.paste(grad, (0, 0), mask)
    glow = glow.filter(ImageFilter.GaussianBlur(26))
    pill.alpha_composite(glow, (pad, pad))
    pill.alpha_composite(g, (pad, pad))
    label = text_patch("免费下载", font(F_YAHEI_B, 46), (255, 255, 255, 255),
                       tracking=6)
    pill.alpha_composite(label, (pad + (pw - label.width) // 2,
                                 pad + (ph - label.height) // 2))
    pill = pill.resize((int(pill.width * 1.0), int(pill.height * 1.0)))

    layers = [
        Layer(960, 330, anim3d(logo, "y", 0.40, 1.1, 12, idle=2.8,
                               period=7.0, phase=1.6),
              0.40, dur=1.1, zoom_from=1.10, rise=0),
        Layer(960, 500,
              anim3d(text_patch("Vchange", font(F_SEGOE_SB, 78), WHITE,
                                tracking=3), "x", 1.70, 0.9, -7,
                     idle=1.5, period=8.5, phase=0.9),
              1.70, dur=0.9, rise=18),
        Layer(960, 660,
              anim3d(pill, "x", 2.60, 0.8, 10, idle=2.0, period=6.0,
                     phase=1.2),
              2.60, dur=0.8, rise=16, zoom_from=0.90),
        Layer(960, 830, text_patch("github.com/Andydot-ui/Vchange",
                                   font(F_SEGOE, 36) if os.path.exists(
                                       "C:/Windows/Fonts/segoeui.ttf") else
                                   font(F_SEGOE_SB, 36), SEC, tracking=4),
              3.60, dur=0.8, rise=14),
        Layer(960, 900, text_patch("免费开源 · GPL-3.0", font(F_YAHEI, 30),
                                   TER, tracking=6), 4.40, dur=0.8, rise=14),
    ]
    return Scene(bg, layers)


F_SEGOE = "C:/Windows/Fonts/segoeui.ttf"


def build_timeline():
    specs = [
        (scene_logo, 7.0),
        (scene_tagline, 7.0),
        (scene_home, 8.0),
        (scene_tools, 10.5),
        (scene_stats, 8.0),
        (scene_gallery, 13.5),
        (scene_gallery2, 13.5),
        (scene_gallery3, 13.5),
        (scene_highlights, 8.5),
        (scene_outbox, 7.0),
        (scene_end, 8.0),
    ]
    scenes, starts = [], []
    t = 0.0
    for fn, dur in specs:
        scenes.append((fn(), dur))
        starts.append(t)
        t += dur - TRANS
    total = starts[-1] + specs[-1][1]

    # Anti-overlap content window:
    #  - delay every element so incoming text appears only after the
    #    cross-fade is half done;
    #  - make outgoing content fade out exactly when the cross-fade starts,
    #    so two scenes' text is never stacked on screen.
    for i, (sc, dur) in enumerate(scenes):
        is_last = i == len(scenes) - 1
        for lay in sc.layers:
            lay.t0 += TRANS * 0.5
            if lay.t1 is None:
                if not is_last:
                    lay.t1 = dur - TRANS
            else:
                lay.t1 = min(lay.t1, dur - TRANS)
    return scenes, starts, total


def render_frame(scenes, starts, total, t):
    idx = 0
    for i, st in enumerate(starts):
        if t >= st:
            idx = i
    cur, dur = scenes[idx]
    a = 0.0
    prev = None
    if idx > 0 and t < starts[idx] + TRANS:
        prev = scenes[idx - 1][0]
        a = ease_in_out((t - starts[idx]) / TRANS)
    f_cur = cur.frame(t - starts[idx])
    if prev is not None:
        f_prev = prev.frame(t - starts[idx - 1])
        f_cur = Image.blend(f_prev, f_cur, a)
    # global fades
    if t < 0.55:
        f_cur = Image.blend(f_cur, Image.new("RGBA", (W, H), (0, 0, 0, 255)),
                            1 - t / 0.55)
    if t > total - 1.3:
        p = (t - (total - 1.3)) / 1.3
        f_cur = Image.blend(f_cur, Image.new("RGBA", (W, H), (0, 0, 0, 255)),
                            ease_in_out(p))
    return f_cur.convert("RGB")


# ----------------------------------------------------------------------------
# Audio: soft ambient pad + bell accents + sub pulse
# ----------------------------------------------------------------------------
SR = 44100

CHORDS = [
    [130.81, 164.81, 196.00, 246.94, 293.66],   # Cmaj9
    [110.00, 130.81, 164.81, 196.00, 246.94],   # Am9-ish
    [87.31, 110.00, 130.81, 164.81, 196.00],    # Fmaj9
    [98.00, 123.47, 146.83, 220.00, 246.94],    # Gadd9
]
CHORD_LEN = 6.6
CHORD_XFADE = 1.6


def pad_segment(freqs, length, detune=1.0012):
    n = int(length * SR)
    t = np.arange(n) / SR
    seg = np.zeros(n)
    for f in freqs:
        seg += np.sin(2 * np.pi * f * detune * t)
        seg += 0.55 * np.sin(2 * np.pi * f * t)
        seg += 0.16 * np.sin(2 * np.pi * 2 * f * t + 0.7)
        seg += 0.07 * np.sin(2 * np.pi * 3 * f * t + 1.3)
    seg /= len(freqs) * 2.0
    env = np.ones(n)
    a = int(CHORD_XFADE * SR)
    env[:a] = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    env[-a:] = (0.5 + 0.5 * np.cos(np.linspace(0, np.pi, a)))
    return seg * env


def fast_lp(x, cutoff):
    """Cheap FIR low-pass (moving average) — fast enough for our needs."""
    n = max(1, int(SR / max(cutoff, 20.0)))
    kernel = np.ones(n) / n
    return np.convolve(x, kernel, mode="same")


def bell(freq, length=2.6):
    n = int(length * SR)
    t = np.arange(n) / SR
    y = (np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.75)
         + 0.45 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t / 0.35)
         + 0.22 * np.sin(2 * np.pi * freq * 5.4 * t) * np.exp(-t / 0.18))
    return y * 0.5


def sub_pulse(length=0.7, f0=58.0):
    n = int(length * SR)
    t = np.arange(n) / SR
    f = f0 * (1 + 0.35 * np.exp(-t / 0.05))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t / 0.22)


def generate_audio(total, scene_starts):
    n = int(total * SR)
    L = np.zeros(n)
    R = np.zeros(n)

    # pad chords looped
    k = 0
    pos = 0.0
    while pos < total:
        freqs = CHORDS[k % len(CHORDS)]
        seg_l = pad_segment(freqs, CHORD_LEN + CHORD_XFADE, detune=1.0012)
        seg_r = pad_segment(freqs, CHORD_LEN + CHORD_XFADE, detune=0.9989)
        i0 = int(pos * SR)
        i1 = min(n, i0 + len(seg_l))
        if i0 >= n:
            break
        L[i0:i1] += seg_l[:i1 - i0] * 0.55
        R[i0:i1] += seg_r[:i1 - i0] * 0.55
        pos += CHORD_LEN
        k += 1

    # bells at each scene start (pentatonic-ish pitches)
    pitches = [523.25, 659.25, 587.33, 783.99, 659.25, 523.25, 587.33, 783.99]
    for i, st in enumerate(scene_starts):
        b = bell(pitches[i % len(pitches)])
        i0 = int(st * SR)
        i1 = min(n, i0 + len(b))
        if i0 < n:
            L[i0:i1] += b[:i1 - i0] * 0.16 * (1.0 if i % 2 == 0 else 0.7)
            R[i0:i1] += b[:i1 - i0] * 0.16 * (0.7 if i % 2 == 0 else 1.0)

    # soft sub pulse every 3.3 s
    p = 1.6
    while p < total - 0.5:
        s = sub_pulse()
        i0 = int(p * SR)
        i1 = min(n, i0 + len(s))
        L[i0:i1] += s[:i1 - i0] * 0.30
        R[i0:i1] += s[:i1 - i0] * 0.30
        p += 3.3

    # air texture
    rng = np.random.default_rng(7)
    air = rng.standard_normal(n)
    air = fast_lp(air, 2400.0) * 0.035
    L += air
    R += np.roll(air, 411)

    # simple stereo reverb-ish tail
    for delay, g in ((0.081, 0.22), (0.137, 0.16), (0.211, 0.11)):
        d = int(delay * SR)
        L[d:] += R[:-d] * g
        R[d:] += L[:-d] * g

    mix = np.stack([L, R], axis=1)
    mix = np.tanh(mix * 0.9)
    peak = np.max(np.abs(mix)) or 1.0
    mix = mix / peak * 0.86

    # fades
    fi = int(1.6 * SR)
    fo = int(3.0 * SR)
    mix[:fi] *= (np.linspace(0, 1, fi) ** 1.5)[:, None]
    mix[-fo:] *= (np.linspace(1, 0, fo) ** 1.5)[:, None]

    pcm = (mix * 32767).astype(np.int16)
    return pcm


def write_wav(path, pcm):
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


# ----------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=None)
    ap.add_argument("--preview", default=None,
                    help="comma-separated timestamps; render PNGs and exit")
    ap.add_argument("--start", type=float, default=0.0,
                    help="start writing at this time (s)")
    args = ap.parse_args()

    out = args.out or os.path.join(ROOT, "promo",
                                   "Vchange-promo-1080p.mp4")
    os.makedirs(os.path.dirname(out), exist_ok=True)

    scenes, starts, total = build_timeline()
    print(f"timeline: {len(scenes)} scenes, total {total:.2f}s, "
          f"{int(total * FPS)} frames")

    if args.preview:
        os.makedirs(os.path.join(ROOT, "promo", "preview"), exist_ok=True)
        for ts in [float(x) for x in args.preview.split(",")]:
            f = render_frame(scenes, starts, total, ts)
            p = os.path.join(ROOT, "promo", "preview", f"t{ts:05.1f}.png")
            f.save(p)
            print("wrote", p)
        return

    silent = out + ".silent.mp4"
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
           "-r", str(FPS), "-i", "-",
           "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "18",
           "-pix_fmt", "yuv420p", "-movflags", "+faststart", silent]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    n_frames = int(math.ceil(total * FPS))
    t0 = 0.0
    for i in range(n_frames):
        t = i / FPS
        frame = render_frame(scenes, starts, total, t)
        proc.stdin.write(frame.tobytes())
        if i % 150 == 0:
            print(f"frame {i}/{n_frames}  t={t:.1f}s", flush=True)
    proc.stdin.close()
    ret = proc.wait()
    if ret != 0:
        raise SystemExit(f"ffmpeg video encode failed: {ret}")
    print("video encoded:", silent)

    wav = out + ".wav"
    pcm = generate_audio(total, starts)
    write_wav(wav, pcm)
    print("audio written:", wav)

    cmd2 = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
            "-i", silent, "-i", wav,
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
            "-shortest", "-movflags", "+faststart", out]
    ret = subprocess.call(cmd2)
    if ret != 0:
        raise SystemExit(f"mux failed: {ret}")
    print("final:", out, os.path.getsize(out), "bytes")

    # keep the wav (useful for re-muxing), drop the silent intermediate
    try:
        os.remove(silent)
    except OSError:
        pass


if __name__ == "__main__":
    main()
