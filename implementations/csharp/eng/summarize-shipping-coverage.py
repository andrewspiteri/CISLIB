"""Summarize Microsoft Cobertura observations without inventing branch identities."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def summarize(language, directories):
    policy = json.loads((language / 'verification/coverage/shipping.json').read_text(encoding='utf-8-sig'))
    sources, assemblies = {}, {}
    for assembly in policy['assemblies']:
        root = language / assembly['sourceRoot']
        entries = {}
        for path in sorted(root.rglob('*.cs')):
            if {'bin', 'obj'}.intersection(path.relative_to(root).parts):
                continue
            relative = path.relative_to(root).as_posix()
            key = 'implementations/csharp/' + path.relative_to(language).as_posix()
            entries[key] = {'path': key, 'sha256': digest(path), 'nonExecutableReason': assembly['nonExecutable'].get(relative)}
            sources[key] = assembly['name']
        if set(assembly['nonExecutable']) - {p.relative_to(root).as_posix() for p in root.rglob('*.cs')}:
            raise ValueError('Non-executable inventory names a missing source')
        evaluated = subprocess.run(['dotnet', 'msbuild', assembly['project'], '-getItem:Compile', '-nologo'], cwd=language,
                                   check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, encoding='utf-8', errors='strict')
        compile_items = json.loads(evaluated.stdout)['Items']['Compile']
        compiled = set()
        for item in compile_items:
            path = Path(item['FullPath'])
            if {'obj', 'bin'}.intersection(path.parts):
                continue
            compiled.add('implementations/csharp/' + path.relative_to(language).as_posix())
        if compiled != set(entries):
            raise ValueError('Evaluated Compile inventory differs from the declared source scope: ' + assembly['name'])
        assemblies[assembly['name']] = {'sources': entries, 'points': {}}
    reports = []
    for directory in directories:
        trx = directory / 'report.trx'
        test = ET.parse(trx)
        counters = next(e for e in test.iter() if e.tag.split('}')[-1] == 'Counters')
        if int(counters.get('total', '0')) <= 0 or counters.get('total') != counters.get('passed'):
            raise ValueError('Missing, failed or skipped test results: ' + str(trx))
        attached = [e.get('href') for e in test.iter() if e.tag.split('}')[-1] == 'A' and e.get('href', '').endswith('.cobertura.xml')]
        if len(attached) != 1:
            raise ValueError('Expected one native Microsoft Cobertura attachment: ' + str(trx))
        matches = list(directory.rglob(attached[0].replace('\\', '/').split('/')[-1]))
        if not matches or len({digest(p) for p in matches}) != 1:
            raise ValueError('Absent or conflicting native attachment')
        report = matches[0]
        xml = ET.parse(report)
        local = set()
        totals = [0, 0, 0, 0]
        for package in xml.findall('./packages/package'):
            name = package.get('name')
            if name not in assemblies:
                raise ValueError('Unexpected shipping assembly: ' + str(name))
            points = assemblies[name]['points']
            for cls in package.findall('./classes/class'):
                filename = cls.get('filename').replace('\\', '/')
                marker = '/implementations/csharp/'
                if marker not in filename:
                    raise ValueError('Unmapped source: ' + filename)
                path = 'implementations/csharp/' + filename.rsplit(marker, 1)[1]
                if sources.get(path) != name:
                    raise ValueError('Source outside inventoried shipping scope: ' + path)
                if assemblies[name]['sources'][path]['nonExecutableReason']:
                    raise ValueError('A declared non-executable source has native sequence points: ' + path)
                for line in cls.findall('./lines/line'):
                    key = (path, cls.get('name'), int(line.get('number')))
                    if (name, key) in local:
                        raise ValueError('Duplicate native class/source/line identity')
                    local.add((name, key))
                    hit = int(line.get('hits')) > 0
                    covered = total = 0
                    if line.get('branch', '').lower() == 'true':
                        match = re.fullmatch(r'.*\((\d+)/(\d+)\)', line.get('condition-coverage', ''))
                        if match is None:
                            raise ValueError('Missing native branch counts')
                        covered, total = map(int, match.groups())
                        if not 0 <= covered <= total or total == 0:
                            raise ValueError('Invalid native branch counts')
                    previous = points.get(key)
                    if previous and previous[2] != total:
                        raise ValueError('Branch denominator drift for ' + str(key))
                    points[key] = (hit or (previous[0] if previous else False), max(covered, previous[1] if previous else 0), total)
                    totals = [totals[0] + int(hit), totals[1] + 1, totals[2] + covered, totals[3] + total]
        native = xml.getroot()
        if totals != [int(native.get(n)) for n in ['lines-covered', 'lines-valid', 'branches-covered', 'branches-valid']]:
            raise ValueError('Parsed denominator does not equal the native report totals')
        reports.append({'trx': str(trx), 'trxSha256': digest(trx), 'cobertura': str(report), 'coberturaSha256': digest(report), 'testsPassed': int(counters.get('passed')), 'nativeTotals': totals})
    def counts(values):
        values = list(values)
        lc, lt, bc, bt = sum(int(v[0]) for v in values), len(values), sum(v[1] for v in values), sum(v[2] for v in values)
        return {'linesCovered': lc, 'linesTotal': lt, 'branchesCoveredLowerBound': bc, 'branchesTotal': bt,
                'lineRate': lc / lt if lt else None, 'branchRateLowerBound': bc / bt if bt else None,
                'thresholdPassed': lt > 0 and bt > 0 and lc >= policy['minimumLineRate'] * lt and bc >= policy['minimumBranchRate'] * bt}
    output = []
    aggregate = []
    for name, assembly in assemblies.items():
        files = []
        for path, source in assembly['sources'].items():
            points = {key: value for key, value in assembly['points'].items() if key[0] == path}
            if not points and not source['nonExecutableReason']:
                raise ValueError('Unmeasured executable source: ' + path)
            files.append(dict(source, **counts(points.values()), uncoveredLines=sorted({key[2] for key, value in points.items() if not value[0]}),
                              partiallyCoveredBranchLines=sorted({key[2] for key, value in points.items() if value[1] < value[2]})))
        aggregate.extend(assembly['points'].values())
        output.append({'assembly': name, **counts(assembly['points'].values()), 'sources': files})
    calibration = [value for key, value in assemblies['CIS.Core']['points'].items() if key[0].endswith('/SeverityTokenParser.cs')]
    if not calibration or sum(v[2] for v in calibration) == 0 or sum(v[1] for v in calibration) == 0:
        raise ValueError('Known branching fixture has no observed branches')
    return {'schema': 'cis.csharp.shipping-coverage-observation/v1', 'policySha256': digest(language / 'verification/coverage/shipping.json'),
            'aggregation': policy['aggregation'], 'exclusions': policy['exclusions'], 'reports': reports, 'assemblies': output,
            'aggregate': counts(aggregate), 'qualified': all(a['thresholdPassed'] for a in output), 'branchCalibration': counts(calibration)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--language-root', type=Path, required=True)
    parser.add_argument('--results', type=Path, nargs='+', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        parser.error('Use a new immutable output path')
    result = summarize(args.language_root.resolve(), [p.resolve() for p in args.results])
    args.output.write_bytes((json.dumps(result, indent=2) + '\n').encode('utf-8'))
    print(json.dumps({'aggregate': result['aggregate'], 'qualified': result['qualified']}))
    return 0 if result['qualified'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
