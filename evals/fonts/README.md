# Fonts

The dataset generator renders with these fonts instead of the system's, so the documents come out the same
on Windows, Linux and in Docker.

| File | Source | Version |
|---|---|---|
| `NotoSans-Regular.ttf` | [notofonts/notofonts.github.io](https://github.com/notofonts/notofonts.github.io/tree/main/fonts/NotoSans/hinted/ttf) | 2.015 |
| `NotoSans-Bold.ttf` | same | 2.015 |
| `NotoSansMono-Regular.ttf` | [same, NotoSansMono](https://github.com/notofonts/notofonts.github.io/tree/main/fonts/NotoSansMono/hinted/ttf) | 2.014 |

Licensed under the [SIL Open Font License 1.1](OFL.txt). The license declares no Reserved Font Name.

**Subset.** Skia's PDF backend embeds the whole font file in every PDF, so the full fonts (~600 KB each)
would make each dataset PDF weigh over a megabyte. These files are cut down to Basic Latin, Latin-1 and a
few punctuation marks plus `€` (~19 KB each), with [fonttools](https://github.com/fonttools/fonttools)
4.66.1:

```bash
pyftsubset <font>.ttf --unicodes="U+0020-007E,U+00A0-00FF,U+2013-2014,U+2018-201D,U+2022,U+2026,U+20AC" \
  --layout-features='kern,liga' --no-hinting --desubroutinize --output-file=<font>.ttf
```

A character outside that range renders as a missing glyph: widen the range and re-subset from the
originals before adding one to the generator.
