import importlib.util
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('summary',Path(__file__).with_name('summarize-pilot.py'))
summary=importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)

class SummaryTests(unittest.TestCase):
    def row(self, seconds, **extra):
        return dict(atUtc=(datetime(2026,11,1,tzinfo=timezone.utc)+timedelta(seconds=seconds)).isoformat(),requestedIntervalSeconds=300,appResidentMemoryBytes=100,databaseAndWalBytes=1000,driveCount=13,processStatus='observed',databaseReadStatus='observed',**extra)
    def report(self, rows):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'evidence.jsonl'
            path.write_text('\n'.join(json.dumps(row) if not isinstance(row,str) else row for row in rows))
            return summary.summarize([path])
    def test_span_with_gap_is_not_complete(self):
        result=self.report([self.row(0),self.row(604800)])
        self.assertEqual(result['spanSeconds'],604800)
        self.assertEqual(result['gapCount'],1)
        self.assertFalse(result['sevenDayLocalCoverageComplete'])
    def test_complete_local_series_still_cannot_approve_release(self):
        result=self.report([self.row(n*300) for n in range(2017)])
        self.assertTrue(result['sevenDayLocalCoverageComplete'])
        self.assertFalse(result['releaseApproved'])
        self.assertEqual(result['databaseAndWalNetChangeBytes'],0)
    def test_missing_bad_duplicate_and_mismatched_records_stay_visible(self):
        missing=self.row(300);missing.update(appResidentMemoryBytes=None,driveCount=12)
        invalid=self.row(600);invalid['databaseAndWalBytes']=-1
        result=self.report([self.row(0),self.row(0),missing,invalid,'not json'])
        self.assertEqual(result['duplicateTimestamps'],1)
        self.assertEqual(len(result['malformedRecords']),2)
        self.assertEqual(result['missingMemorySamples'],1)
        self.assertEqual(result['workloadMismatchSamples'],1)
        self.assertFalse(result['sevenDayLocalCoverageComplete'])
    def test_infinite_interval_cannot_hide_gaps(self):
        row=self.row(0);row['requestedIntervalSeconds']=float('inf')
        result=self.report([row])
        self.assertEqual(result['validSamples'],0)
        self.assertIsNone(result['peakApiResidentMemoryBytes'])
        self.assertFalse(result['sevenDayLocalCoverageComplete'])

if __name__=='__main__':
    unittest.main()
