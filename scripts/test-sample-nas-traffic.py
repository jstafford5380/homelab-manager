import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('traffic',Path(__file__).with_name('sample-nas-traffic.py'))
traffic=importlib.util.module_from_spec(spec)
spec.loader.exec_module(traffic)

class TrafficTests(unittest.TestCase):
    def test_only_exact_remote_ip_and_port_are_counted(self):
        rows=traffic.parse_counters(',bytes_out,bytes_in,\nHomelab.Api.1,999,888,\ntcp4 10.0.0.2:1000<->192.0.2.1:443,12,34,\ntcp4 10.0.0.2:1001<->192.0.2.1:444,56,78,\ntcp4 10.0.0.2:1002<->192.0.2.11:443,90,100,\n','192.0.2.1',443)
        self.assertEqual(len(rows),1)
        self.assertEqual(rows[0]['observedBytesSent'],12)
        self.assertEqual(rows[0]['observedBytesReceived'],34)
        self.assertNotIn('192.0.2.1',str(rows))
    def test_missing_socket_is_not_zero_traffic(self):
        self.assertEqual(traffic.parse_counters(',bytes_in,bytes_out,\nprocess,100,200,\n','192.0.2.1',443),[])
    def test_malformed_or_missing_counters_rejected(self):
        for text in ['no useful columns', ',bytes_in,bytes_out,\ntcp4 10.0.0.2:1000<->192.0.2.1:443,,2,']:
            with self.assertRaises(ValueError): traffic.parse_counters(text,'192.0.2.1',443)
    def test_changed_pid_never_runs_nettop(self):
        with patch.object(traffic.pilot,'process_sample',return_value={'processIdentityHash':'changed'}),patch.object(traffic.subprocess,'run') as run:
            row=traffic.sample(123,'192.0.2.1',443,'original')
            self.assertEqual(row['status'],'process-unavailable-or-changed')
            self.assertEqual(row['sockets'],[])
            run.assert_not_called()

if __name__=='__main__': unittest.main()
