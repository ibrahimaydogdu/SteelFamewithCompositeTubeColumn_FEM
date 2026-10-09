"""Minimal Markdown -> HTML for the user guide (headings, tables, nested lists, code, quotes, inline markup, links)."""
import html, re, sys

src, dst, title = sys.argv[1], sys.argv[2], sys.argv[3]
lines = open(src, encoding='utf-8').read().replace('\r\n', '\n').split('\n')


def slug(t):
    t = re.sub(r'<[^>]+>', '', t).strip().lower()
    t = re.sub(r'[^\w\- ]', '', t, flags=re.UNICODE)
    return t.replace(' ', '-')


def inline(t):
    codes = []
    def keep(m):
        codes.append('<code>' + html.escape(m.group(1)) + '</code>')
        return '\x00%d\x00' % (len(codes) - 1)
    t = re.sub(r'`([^`]+)`', keep, t)
    t = html.escape(t, quote=False)
    t = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', lambda m: '<a href="%s">%s</a>' % (m.group(2).replace('.md', '.html') if m.group(2).endswith('.md') else m.group(2), m.group(1)), t)
    t = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'(?<![\w*])\*([^*\n]+)\*(?![\w*])', r'<em>\1</em>', t)
    t = t.replace('[ ]', '&#9744;')
    return re.sub(r'\x00(\d+)\x00', lambda m: codes[int(m.group(1))], t)


out = []
i = 0
list_stack = []   # (indent, tag)


def close_lists(to_indent=-1):
    while list_stack and list_stack[-1][0] > to_indent:
        out.append('</li></%s>' % list_stack.pop()[1])


while i < len(lines):
    l = lines[i]
    if l.startswith('```'):
        close_lists()
        code = []
        i += 1
        while i < len(lines) and not lines[i].startswith('```'):
            code.append(lines[i]); i += 1
        out.append('<pre><code>' + html.escape('\n'.join(code)) + '</code></pre>')
        i += 1
        continue
    m = re.match(r'^(#{1,4}) (.*)$', l)
    if m:
        close_lists()
        n = len(m.group(1)); txt = inline(m.group(2))
        out.append('<h%d id="%s">%s</h%d>' % (n, slug(m.group(2)), txt, n))
        i += 1; continue
    if l.strip() == '---':
        close_lists(); out.append('<hr>'); i += 1; continue
    if l.startswith('|'):
        close_lists()
        rows = []
        while i < len(lines) and lines[i].startswith('|'):
            rows.append(lines[i]); i += 1
        cells = lambda r: [c.strip() for c in re.split(r'(?<!\\)\|', r.strip()[1:-1])]
        out.append('<table><thead><tr>' + ''.join('<th>%s</th>' % inline(c) for c in cells(rows[0])) + '</tr></thead><tbody>')
        for r in rows[2:]:
            out.append('<tr>' + ''.join('<td>%s</td>' % inline(c.replace('\\|', '|')) for c in cells(r)) + '</tr>')
        out.append('</tbody></table>')
        continue
    if l.startswith('> '):
        close_lists()
        q = []
        while i < len(lines) and lines[i].startswith('>'):
            q.append(lines[i][1:].strip()); i += 1
        out.append('<blockquote>' + inline(' '.join(q)) + '</blockquote>')
        continue
    m = re.match(r'^(\s*)([-*]|\d+\.) (.*)$', l)
    if m:
        ind = len(m.group(1)); tag = 'ol' if m.group(2)[0].isdigit() else 'ul'
        if not list_stack or ind > list_stack[-1][0]:
            out.append('<%s><li>' % tag); list_stack.append((ind, tag))
        else:
            close_lists(ind)
            if list_stack and list_stack[-1][0] == ind:
                out.append('</li><li>')
            else:
                out.append('<%s><li>' % tag); list_stack.append((ind, tag))
        out.append(inline(m.group(3)))
        i += 1; continue
    if l.strip() == '':
        # a blank line inside a list keeps the list open if the next line is indented or a list item
        nxt = lines[i + 1] if i + 1 < len(lines) else ''
        if list_stack and (nxt.startswith(' ') or re.match(r'^(\s*)([-*]|\d+\.) ', nxt)):
            i += 1; continue
        close_lists(); i += 1; continue
    if list_stack and l.startswith(' '):
        out.append('<br>' + inline(l.strip())); i += 1; continue
    close_lists()
    para = [l]; i += 1
    while i < len(lines) and lines[i].strip() and not re.match(r'^(#|\||>|```|\s*([-*]|\d+\.) |---)', lines[i]):
        para.append(lines[i]); i += 1
    out.append('<p>' + inline(' '.join(para)) + '</p>')
close_lists()

css = """body{font-family:Segoe UI,Arial,sans-serif;max-width:1000px;margin:24px auto;padding:0 16px;line-height:1.5;color:#222}
table{border-collapse:collapse;margin:8px 0;width:100%}th,td{border:1px solid #bbb;padding:4px 8px;vertical-align:top;text-align:left}
th{background:#eef2f7}code{background:#f2f2f2;padding:1px 4px;border-radius:3px;font-family:Consolas,monospace}
pre{background:#f2f2f2;padding:8px;overflow-x:auto}blockquote{border-left:4px solid #d9a400;background:#fff8e1;margin:8px 0;padding:6px 12px}
h1{border-bottom:2px solid #446}h2{border-bottom:1px solid #ccd;margin-top:32px}a{color:#1a5fb4}
@media print{h2{page-break-before:auto}a{color:#000}}"""
open(dst, 'w', encoding='utf-8').write('<!DOCTYPE html>\n<html lang="tr"><head><meta charset="utf-8"><title>%s</title><style>%s</style></head><body>\n%s\n</body></html>\n'
                                       % (html.escape(title), css, '\n'.join(out)))
print('ok', len(out))
