import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from digital_storage_tools.trx import validate, NS


class TrxTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name) / 'report.trx'
        self.root = ET.fromstring('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary><Results><UnitTestResult testId="t" executionId="e" outcome="Passed"/></Results><TestDefinitions><UnitTest id="t"><Execution id="e"/><TestMethod className="X" name="Y"/></UnitTest></TestDefinitions></TestRun>''')

    def check(self):
        ET.ElementTree(self.root).write(self.path)
        return validate(self.path)

    def test_valid(self):
        self.assertEqual(self.check(), 1)

    def test_approved_identity_list_must_match_actual_report(self):
        self.root.find('v:TestDefinitions/v:UnitTest/v:TestMethod', NS).set('className', 'Api.Tests')
        ET.ElementTree(self.root).write(self.path)
        with self.assertRaisesRegex(ValueError, 'tests differ from reviewed list'):
            validate(self.path, ['Api.Tests.Other'])

    def test_semantically_invalid_reports_fail(self):
        for case in ('missing', 'missing-field', 'negative', 'outcome', 'summary', 'id', 'duplicate', 'duplicate-definition', 'execution'):
            with self.subTest(case=case):
                self.setUp()
                results = self.root.find('v:Results', NS)
                item = results[0]
                counters = self.root.find('v:ResultSummary/v:Counters', NS)
                if case == 'missing': results.remove(item)
                if case == 'missing-field': del counters.attrib['executed']
                if case == 'negative': counters.set('failed', '-1')
                if case == 'outcome': item.set('outcome', 'Failed')
                if case == 'summary': self.root.find('v:ResultSummary', NS).set('outcome', 'Failed')
                if case == 'id': item.set('testId', 'other')
                if case == 'duplicate': results.append(ET.fromstring(ET.tostring(item)))
                if case == 'duplicate-definition':
                    definitions = self.root.find('v:TestDefinitions', NS)
                    definitions.append(ET.fromstring(ET.tostring(definitions[0])))
                if case == 'execution': item.set('executionId', 'other')
                with self.assertRaises((ValueError, KeyError)): self.check()
