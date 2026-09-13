"""Render the inventory from the same catalog the Editor and PowerShell use. --check is read-only."""
import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
DOC = ROOT / 'docs/editor-tools'


def load(name):
    return json.loads((DOC / name).read_text(encoding='utf-8-sig'))


def cell(value):
    if isinstance(value, list):
        value = '、'.join(value)
    return str(value or '—').replace('|', '\\|').replace('\n', '<br>')


def table(headers, rows):
    return '\n'.join(['| ' + ' | '.join(headers) + ' |', '| ' + ' | '.join(['---'] * len(headers)) + ' |'] +
                     ['| ' + ' | '.join(cell(v) for v in row) + ' |' for row in rows]) + '\n'


def generate():
    catalog, legacy = load('catalog.json'), load('legacy-inventory.json')
    tools = {t['id']: t for t in catalog['tools']}
    if len(tools) != len(catalog['tools']):
        raise ValueError('Duplicate tool ID')
    if len(legacy['menus']) != 59 or any(m.get('newId') not in tools for m in legacy['menus']):
        raise ValueError('Every one of the 59 previous menu commands must have a destination')
    for t in tools.values():
        if not t.get('description') or not t.get('impact'):
            raise ValueError('Undocumented tool: ' + t['id'])
    output = '# EditorTool 完整清单\n\n本文件由 `Tools/Update-EditorToolDocs.py` 从共享清单生成。不要直接编辑。\n\n'
    output += '## 当前工具\n\n'
    output += table(['ID', '名称 / 用途', '分类 / 使用者', '参数', '运行条件', '影响 / 产物'], [
        (t['id'], t['name'] + '：' + t['description'], t['category'] + ' / ' + t['audience'], t.get('parameters', []),
         ', '.join(filter(None, [t.get('condition'), '图形设备' if t.get('graphics') else '', '已打开的 Editor' if t.get('interactive') else '',
                                 '外部参考工程' if t.get('externalSource') else '', '等待 Play Mode 完成' if t.get('asynchronous') else ''])),
         t['impact'] + '；' + t['output']) for t in tools.values()])
    output += '\n## 构建配置\n\n正式流程包含 Boot、MainMenu、Gameplay；专项场景保持独立。所有配置都使用统一构建服务。\n\n'
    output += table(['Profile', 'Development', '测试程序集', '场景', '宏', '输出'], [
        (b['id'], b['development'], b['testAssemblies'], b['scenes'], b['defines'], b['output']) for b in catalog['builds']])
    output += '\n## 59 个旧菜单的去向\n\n旧菜单已移除；兼容期为本轮工具整理后的一个交付版本。\n\n'
    output += table(['旧菜单', '旧类 / 方法', '新 ID / 配置', '处置', '来源'], [
        (m['oldMenu'], m['method'], m['newId'] + ((' -Profile ' + m['profile']) if m.get('profile') else ''), m['disposition'], m['path']) for m in legacy['menus']])
    output += '\n## Editor 集成与自动钩子\n\n这些文件没有独立按钮也不等于无用。属性绘制器、Inspector、构建前检查和 Steam AppID 后处理继续自动生效。\n\n'
    output += table(['文件', '主要类型', '自动集成', '处置'], [(e['path'], e['types'], e['hooks'], e['disposition']) for e in legacy['editorIntegrations']])
    output += '\n## 额外批处理入口 / 无参公共方法\n\n此表是旧代码入口清单，不表示每个辅助方法都应直接由 AI 调用；日常调用以稳定 ID 为准。需要写入的维护方法使用统一入口的 `-Apply`。\n\n'
    output += table(['来源', '方法', '处置'], [(b['path'], b['method'], b['disposition']) for b in legacy['batchCandidates']])
    output += '\n## 外部脚本\n\n28 个旧 PowerShell 入口保留参数并转发至统一执行器；实际场景断言位于 `Tools/Scenarios`。进程公共能力在 `Tools/ProjectTools.psm1`。\n\n'
    rows = []
    for path in legacy['scripts']:
        match = next((t for t in tools.values() if t.get('script') == path), None)
        disposition = ('兼容转发 → ' + match['id']) if match else ('删除：I2 一次性提取已完成，保留历史记录' if 'Export-LocalizationMigration' in path else '保留：Nordic 显式来源导入；需要 --source，属于维护')
        rows.append((path, disposition))
    output += table(['旧脚本', '处置'], rows)
    return output


def audit_sources():
    menu_count, build_count = 0, 0
    for path in (ROOT / 'Assets/_Project').rglob('*.cs'):
        if 'Rewired' in path.parts:
            continue
        source = path.read_text(encoding='utf-8-sig')
        for match in re.finditer(r'\[(?:UnityEditor\.)?MenuItem\((.*?)\)\]', source, re.S):
            if not path.name == 'ProjectToolWindow.cs':
                raise ValueError('Project menu escaped the unified root: ' + str(path))
            if not re.search(r',\s*true\b', match.group(1)):
                menu_count += 1
        build_count += source.count('BuildPipeline.BuildPlayer(')
        if 'ExecuteMenuItem(' in source:
            raise ValueError('Internal project menu-text dependency: ' + str(path))
    if (menu_count, build_count) != (18, 1):
        raise ValueError(f'Expected 18 shortcuts / 1 build implementation; got {menu_count}/{build_count}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    audit_sources()
    text = generate()
    path = DOC / 'inventory.md'
    if args.check:
        if not path.exists() or path.read_text(encoding='utf-8') != text:
            sys.exit('Tool inventory is stale; run Tools/Update-EditorToolDocs.py')
    else:
        path.write_text(text, encoding='utf-8', newline='\n')
    print('Editor tools: 59 old menus mapped, 18 shortcuts, 1 build implementation; documentation consistent.')
