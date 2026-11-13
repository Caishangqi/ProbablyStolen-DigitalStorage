"""Validate VSTest reports before treating a run as successful."""
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {'v': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def validate(path: Path, approved: list[str] | None = None) -> int:
    root = ET.parse(path).getroot()
    summary = root.find('v:ResultSummary', NS)
    if summary is None or summary.get('outcome') != 'Completed':
        raise ValueError('Test summary is not completed')
    counters = summary.find('v:Counters', NS)
    if counters is None:
        raise ValueError('Test counters are missing')
    names = ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
             'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')
    values = {name: int(counters.attrib[name]) for name in names}
    if any(value < 0 for value in values.values()) or values['total'] < 1:
        raise ValueError('Invalid or empty test counters')
    if values['total'] != values['executed'] or values['total'] != values['passed'] or any(values[name] for name in names[3:]):
        raise ValueError('Failed, skipped or inconsistent test counters')
    results = root.findall('v:Results/v:UnitTestResult', NS)
    definitions = root.findall('v:TestDefinitions/v:UnitTest', NS)
    if len(results) != values['total'] or len(definitions) != values['total']:
        raise ValueError('Test result or definition count mismatch')
    defined = {}
    for item in definitions:
        test_id = item.attrib['id']
        execution = item.find('v:Execution', NS)
        method = item.find('v:TestMethod', NS)
        if test_id in defined or execution is None or method is None:
            raise ValueError('Invalid or duplicated test definition')
        defined[test_id] = (execution.attrib['id'], method.attrib['className'].split(',')[0] + '.' + method.attrib['name'])
    seen = set()
    executions = set()
    tests = []
    for item in results:
        test_id, execution_id = item.attrib['testId'], item.attrib['executionId']
        if item.attrib['outcome'] != 'Passed' or test_id in seen or execution_id in executions or test_id not in defined or defined[test_id][0] != execution_id:
            raise ValueError('Test result does not match its definition')
        seen.add(test_id)
        executions.add(execution_id)
        tests.append(defined[test_id][1])
    if approved is not None and sorted(tests) != sorted(approved):
        raise ValueError('TRX tests differ from reviewed list')
    return values['total']
