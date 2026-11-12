//! Spike: Parley behind a C interface (see ../README.md). Text is passed as UTF-16 and offsets are UTF-16; Parley works on UTF-8 bytes,
//! so every paragraph keeps two index tables. No system fonts: the fonts come from the bytes the caller registers.

use parley::fontique::{Blob, FontStyle};
use parley::{
    Affinity, Cursor, FontContext, FontFamily, FontWeight, Layout, LayoutContext, PositionedLayoutItem, StyleProperty,
};
use std::collections::HashMap;
use std::ffi::c_char;

#[repr(C)]
pub struct PcRun {
    pub start: u32,
    pub len: u32,
    /// 0 = Carlito, 1 = Caladea
    pub family: u32,
    pub bold: u32,
    pub italic: u32,
    pub size: f32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct PcLine {
    pub start: u32,
    pub len: u32,
    pub ascent: f32,
    pub descent: f32,
    pub width: f32,
    /// Middle of the line box in Parley's coordinates (the caller hit-tests at this y).
    pub y_mid: f32,
}

#[repr(C)]
pub struct PcRect {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
}

#[repr(C)]
pub struct PcGlyph {
    pub font_id: u32,
    pub glyph_id: u32,
    pub x: f32,
    /// Relative to the baseline of the line.
    pub y: f32,
    pub size: f32,
}

pub struct PcCtx {
    font_cx: FontContext,
    layout_cx: LayoutContext<()>,
    blob_to_font: HashMap<u64, u32>,
    next_font: u32,
}

pub struct PcPara {
    layout: Layout<()>,
    u16_to_u8: Vec<u32>,
    u8_to_u16: Vec<u32>,
    lines: Vec<PcLine>,
    font_ids: HashMap<u64, u32>,
}

static PROFILE_RELEASE: &[u8] = b"release\0";
static PROFILE_DEBUG: &[u8] = b"debug\0";

#[no_mangle]
pub extern "C" fn pc_build_profile() -> *const c_char {
    if cfg!(debug_assertions) { PROFILE_DEBUG.as_ptr() as *const c_char } else { PROFILE_RELEASE.as_ptr() as *const c_char }
}

#[no_mangle]
pub extern "C" fn pc_ctx_create() -> *mut PcCtx {
    Box::into_raw(Box::new(PcCtx {
        font_cx: FontContext::default(),
        layout_cx: LayoutContext::new(),
        blob_to_font: HashMap::new(),
        next_font: 0,
    }))
}

/// # Safety
/// `ctx` comes from `pc_ctx_create`, `data` points to `len` bytes, `font_id` is writable.
#[no_mangle]
pub unsafe extern "C" fn pc_ctx_add_font(ctx: *mut PcCtx, data: *const u8, len: usize, font_id: *mut u32) -> i32 {
    let ctx = &mut *ctx;
    let bytes = std::slice::from_raw_parts(data, len).to_vec();
    let blob: Blob<u8> = Blob::from(bytes);
    let id = blob.id();
    let registered = ctx.font_cx.collection.register_fonts(blob, None);
    if registered.is_empty() {
        return 1;
    }
    let fid = ctx.next_font;
    ctx.next_font += 1;
    ctx.blob_to_font.insert(id, fid);
    *font_id = fid;
    0
}

/// # Safety
/// `text` points to `len` UTF-16 units, `runs` to `run_count` runs that partition the text.
#[no_mangle]
pub unsafe extern "C" fn pc_para_layout(
    ctx: *mut PcCtx,
    text: *const u16,
    len: usize,
    runs: *const PcRun,
    run_count: usize,
    width: f32,
) -> *mut PcPara {
    let ctx = &mut *ctx;
    let units = std::slice::from_raw_parts(text, len);
    let runs = std::slice::from_raw_parts(runs, run_count);
    let string = String::from_utf16_lossy(units);

    // Index tables between UTF-16 offsets and UTF-8 byte offsets.
    let mut u16_to_u8 = vec![0u32; len + 1];
    let mut u8_to_u16 = vec![0u32; string.len() + 1];
    let (mut b, mut u) = (0usize, 0usize);
    for ch in string.chars() {
        let (n8, n16) = (ch.len_utf8(), ch.len_utf16());
        for k in 0..n16 {
            u16_to_u8[u + k] = b as u32;
        }
        for k in 0..n8 {
            u8_to_u16[b + k] = u as u32;
        }
        b += n8;
        u += n16;
    }
    u16_to_u8[len] = b as u32;
    u8_to_u16[b] = u as u32;

    let default_size = runs.first().map(|r| r.size).unwrap_or(11.0);
    let mut builder = ctx.layout_cx.ranged_builder(&mut ctx.font_cx, &string, 1.0, false);
    builder.push_default(StyleProperty::FontSize(default_size));
    builder.push_default(StyleProperty::FontFamily(FontFamily::named("Caladea")));
    for r in runs {
        if r.len == 0 {
            continue;
        }
        let range = u16_to_u8[r.start as usize] as usize..u16_to_u8[(r.start + r.len) as usize] as usize;
        builder.push(StyleProperty::FontFamily(FontFamily::named(if r.family == 0 { "Carlito" } else { "Caladea" })), range.clone());
        builder.push(StyleProperty::FontSize(r.size), range.clone());
        builder.push(StyleProperty::FontWeight(FontWeight::new(if r.bold != 0 { 700.0 } else { 400.0 })), range.clone());
        builder.push(StyleProperty::FontStyle(if r.italic != 0 { FontStyle::Italic } else { FontStyle::Normal }), range);
    }
    let mut layout: Layout<()> = builder.build(&string);
    layout.break_all_lines(Some(width));

    let mut lines = Vec::new();
    for line in layout.lines() {
        let range = line.text_range();
        let m = line.metrics();
        let start = u8_to_u16[range.start];
        let end = u8_to_u16[range.end];
        let ascent = m.baseline - m.content_block_min_coord;
        lines.push(PcLine {
            start,
            len: end - start,
            ascent,
            descent: m.line_height - ascent,
            width: m.advance - m.hanging_advance,
            y_mid: (m.block_min_coord + m.block_max_coord) / 2.0,
        });
    }
    if lines.is_empty() {
        lines.push(PcLine { start: 0, len: 0, ascent: default_size * 0.9, descent: default_size * 0.3, width: 0.0, y_mid: 0.0 });
    }
    Box::into_raw(Box::new(PcPara { layout, u16_to_u8, u8_to_u16, lines, font_ids: ctx.blob_to_font.clone() }))
}

/// # Safety
/// `para` comes from `pc_para_layout`; `out` has room for `cap` lines.
#[no_mangle]
pub unsafe extern "C" fn pc_para_lines(para: *const PcPara, out: *mut PcLine, cap: usize) -> usize {
    let p = &*para;
    for (i, l) in p.lines.iter().enumerate().take(cap) {
        *out.add(i) = *l;
    }
    p.lines.len()
}

/// # Safety
/// `para` comes from `pc_para_layout`, `offset` is writable.
#[no_mangle]
pub unsafe extern "C" fn pc_para_hit_test(para: *const PcPara, x: f32, y: f32, offset: *mut u32) -> i32 {
    let p = &*para;
    let cursor = Cursor::from_point(&p.layout, x, y);
    *offset = p.u8_to_u16[cursor.index().min(p.u8_to_u16.len() - 1)];
    0
}

/// # Safety
/// `para` comes from `pc_para_layout`, `out` is writable.
#[no_mangle]
pub unsafe extern "C" fn pc_para_caret(para: *const PcPara, offset: u32, out: *mut PcRect) -> i32 {
    let p = &*para;
    let byte = p.u16_to_u8[(offset as usize).min(p.u16_to_u8.len() - 1)] as usize;
    let cursor = Cursor::from_byte_index(&p.layout, byte, Affinity::Downstream);
    let g = cursor.geometry(&p.layout, 1.0);
    *out = PcRect { x: g.x0 as f32, y: g.y0 as f32, w: (g.x1 - g.x0) as f32, h: (g.y1 - g.y0) as f32 };
    0
}

/// # Safety
/// `para` comes from `pc_para_layout`; `out` has room for `cap` glyphs. Returns the number of glyphs of the line range.
#[no_mangle]
pub unsafe extern "C" fn pc_para_glyphs(para: *const PcPara, first_line: u32, line_count: u32, out: *mut PcGlyph, cap: usize) -> usize {
    let p = &*para;
    let mut n = 0usize;
    for (i, line) in p.layout.lines().enumerate() {
        if (i as u32) < first_line || (i as u32) >= first_line + line_count {
            continue;
        }
        for item in line.items() {
            if let PositionedLayoutItem::GlyphRun(gr) = item {
                let font_id = p.font_ids.get(&gr.run().font().data.id()).copied().unwrap_or(u32::MAX);
                let size = gr.run().font_size();
                let baseline = gr.baseline();
                for g in gr.positioned_glyphs() {
                    if n < cap {
                        *out.add(n) = PcGlyph { font_id, glyph_id: g.id, x: g.x, y: g.y - baseline, size };
                    }
                    n += 1;
                }
            }
        }
    }
    n
}

/// # Safety
/// `para` comes from `pc_para_layout` and is freed once.
#[no_mangle]
pub unsafe extern "C" fn pc_para_free(para: *mut PcPara) {
    if !para.is_null() {
        drop(Box::from_raw(para));
    }
}

/// # Safety
/// `ctx` comes from `pc_ctx_create` and is freed once.
#[no_mangle]
pub unsafe extern "C" fn pc_ctx_free(ctx: *mut PcCtx) {
    if !ctx.is_null() {
        drop(Box::from_raw(ctx));
    }
}
