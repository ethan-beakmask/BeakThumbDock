"""Builds docs/manual.html from docs/manual.md (no third-party packages).

Usage:  python tools/build-manual.py

Supports the Markdown subset the manual uses: # headings, paragraphs, - and 1. lists (one level),
tables, fenced code blocks, and inline `code`, **bold**, [text](url).
"""
import html
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs" / "manual.md"
TARGET = ROOT / "docs" / "manual.html"

CSS = """
:root { --bg:#ffffff; --fg:#1f2328; --muted:#59636e; --line:#d0d7de; --soft:#f6f8fa; --accent:#b35900; --link:#0969da; }
@media (prefers-color-scheme: dark) {
  :root { --bg:#16181c; --fg:#e6e8eb; --muted:#9aa4af; --line:#30363d; --soft:#1f2329; --accent:#ffa028; --link:#6cb6ff; }
}
* { box-sizing: border-box; }
body { margin:0; background:var(--bg); color:var(--fg); line-height:1.75;
  font-family:"Segoe UI","Microsoft JhengHei","PingFang TC","Noto Sans TC",sans-serif; }
.page { display:grid; grid-template-columns:260px minmax(0,1fr); gap:40px; max-width:1180px; margin:0 auto; padding:32px 24px 80px; }
nav { position:sticky; top:24px; align-self:start; max-height:calc(100vh - 48px); overflow:auto; font-size:14px; }
nav .title { font-weight:600; color:var(--muted); margin-bottom:8px; }
nav a { display:block; padding:3px 0 3px 10px; border-left:2px solid var(--line); color:var(--fg); text-decoration:none; }
nav a:hover { border-left-color:var(--accent); color:var(--accent); }
main { min-width:0; }
h1 { font-size:30px; margin:0 0 8px; }
h2 { font-size:22px; margin:44px 0 12px; padding-bottom:6px; border-bottom:1px solid var(--line); }
h3 { font-size:17px; margin:28px 0 8px; }
p, ul, ol { margin:10px 0; }
li { margin:4px 0; }
a { color:var(--link); }
code { font-family:Consolas,"Cascadia Mono",monospace; font-size:.92em; background:var(--soft); border:1px solid var(--line); border-radius:4px; padding:1px 5px; }
pre { background:var(--soft); border:1px solid var(--line); border-radius:6px; padding:12px 14px; overflow-x:auto; }
pre code { background:none; border:0; padding:0; }
.table { overflow-x:auto; margin:12px 0; }
table { border-collapse:collapse; width:100%; font-size:15px; }
th, td { border:1px solid var(--line); padding:7px 10px; text-align:left; vertical-align:top; }
th { background:var(--soft); white-space:nowrap; }
strong { color:var(--accent); }
@media (max-width: 820px) {
  .page { grid-template-columns:1fr; gap:8px; padding:20px 16px 60px; }
  nav { position:static; max-height:none; border-bottom:1px solid var(--line); padding-bottom:12px; }
}
"""


def inline(text: str) -> str:
    """Escapes text and applies `code`, **bold** and [text](url)."""
    parts = re.split(r"(`[^`]+`)", text)
    out = []
    for part in parts:
        if len(part) >= 2 and part.startswith("`") and part.endswith("`"):
            out.append("<code>" + html.escape(part[1:-1]) + "</code>")
            continue
        s = html.escape(part)
        s = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", s)
        s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', s)
        out.append(s)
    return "".join(out)


def cells(line: str) -> list[str]:
    return [c.strip() for c in line.strip().strip("|").split("|")]


def convert(lines: list[str]) -> tuple[str, str, list[tuple[str, str]]]:
    body, toc, title = [], [], "ThumbDock"
    i, section = 0, 0
    while i < len(lines):
        line = lines[i]
        if not line.strip():
            i += 1
        elif line.startswith("```"):
            i += 1
            block = []
            while i < len(lines) and not lines[i].startswith("```"):
                block.append(lines[i])
                i += 1
            i += 1
            body.append("<pre><code>" + html.escape("\n".join(block)) + "</code></pre>")
        elif m := re.match(r"(#{1,3}) (.+)", line):
            level, text = len(m.group(1)), m.group(2)
            if level == 1:
                title = text
                body.append(f"<h1>{inline(text)}</h1>")
            else:
                section += 1
                anchor = f"s{section}"
                if level == 2:
                    toc.append((anchor, text))
                body.append(f'<h{level} id="{anchor}">{inline(text)}</h{level}>')
            i += 1
        elif line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append(cells(lines[i]))
                i += 1
            head, data = rows[0], rows[2:]  # rows[1] is the |---| separator
            t = ["<div class=\"table\"><table><thead><tr>"]
            t += [f"<th>{inline(c)}</th>" for c in head]
            t.append("</tr></thead><tbody>")
            for row in data:
                t.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in row) + "</tr>")
            t.append("</tbody></table></div>")
            body.append("".join(t))
        elif re.match(r"(- |\d+\. )", line):
            ordered = not line.startswith("- ")
            pattern = r"\d+\. (.*)" if ordered else r"- (.*)"
            items = []
            while i < len(lines) and (m := re.match(pattern, lines[i])):
                items.append(f"<li>{inline(m.group(1))}</li>")
                i += 1
            tag = "ol" if ordered else "ul"
            body.append(f"<{tag}>" + "".join(items) + f"</{tag}>")
        else:
            para = []
            while i < len(lines) and lines[i].strip() and not re.match(r"(#{1,3} |\||```|- |\d+\. )", lines[i]):
                para.append(lines[i].strip())
                i += 1
            body.append("<p>" + inline("".join(para)) + "</p>")
    return title, "\n".join(body), toc


def main() -> None:
    title, body, toc = convert(SOURCE.read_text(encoding="utf-8").splitlines())
    nav = "".join(f'<a href="#{a}">{html.escape(t)}</a>' for a, t in toc)
    page = f"""<!doctype html>
<html lang="zh-Hant">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(title)}</title>
<style>{CSS}</style>
</head>
<body>
<div class="page">
<nav><div class="title">目錄</div>{nav}</nav>
<main>
{body}
</main>
</div>
</body>
</html>
"""
    TARGET.write_text(page, encoding="utf-8", newline="\n")
    print(f"{TARGET.relative_to(ROOT)}: {len(toc)} sections, {len(page)} characters")


if __name__ == "__main__":
    main()
