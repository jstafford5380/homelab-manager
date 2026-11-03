import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location("wrapper", pathlib.Path(__file__).with_name("smart-read-wrapper.py"))
wrapper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wrapper)


class WrapperTests(unittest.TestCase):
    def test_only_allowed_commands(self):
        self.assertEqual(wrapper.command("smartctl --scan", set())[-1], "--scan")
        self.assertEqual(wrapper.command("smartctl --json -x -n standby /dev/sda", {"/dev/sda"})[-5:],
                         ["--json", "-x", "-n", "standby", "/dev/sda"])

    def test_rejects_unsafe_commands_and_unscanned_devices(self):
        for value in ["", "sh", "smartctl -t long /dev/sda", "smartctl --scan; id",
                      "smartctl --json -x /dev/sda", "smartctl --json -x -n standby /dev/sdb",
                      "smartctl --json -x -n standby /dev/sda;id",
                      "smartctl --json -x -n standby /dev/../etc/passwd",
                      "smartctl --json -x -n standby /dev/sda\n",
                      "smartctl --json -x -n standby /dev/sda -t short",
                      "smartctl --json -x -n standby /dev/$(id)"]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                wrapper.command(value, {"/dev/sda"})

    def test_scan_is_not_shell_input(self):
        self.assertEqual(wrapper.devices("/dev/sda -d sat # a\n/dev/nvme0 -d nvme\nbad line"), {"/dev/sda", "/dev/nvme0"})


if __name__ == "__main__":
    unittest.main()
