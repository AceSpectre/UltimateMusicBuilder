"""Exercise setup with fake package managers; never install system packages.

Run with: python3 scripts/test-setup.py
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
MOCK = r'''
import json, os
from pathlib import Path
import sys

base = Path(os.environ['UMB_SETUP_TEST_DIR'])
name = Path(sys.argv[0]).name
args = sys.argv[1:]
with (base / 'calls.jsonl').open('a') as f:
    f.write(json.dumps([name, *args]) + '\n')

def install(name):
    (base / 'bin' / name).symlink_to(base / 'mock.py')

if name == 'uname':
    print(os.environ['UMB_SETUP_TEST_OS'])
elif name == 'sudo':
    os.execv(str(base / 'bin' / args[0]), args)
elif name in ('brew', 'apt-get', 'dnf', 'pacman'):
    if os.environ.get('UMB_SETUP_TEST_FAIL') == '1':
        sys.exit(7)
    if 'ffmpeg' in args or 'ffmpeg-free' in args:
        for tool in ('ffmpeg', 'ffprobe', 'ffplay'):
            if not (base / 'bin' / tool).exists(): install(tool)
    if 'pipx' in args or 'python-pipx' in args:
        if not (base / 'bin' / 'pipx').exists(): install('pipx')
elif name == 'pipx':
    if args[0] == 'environment':
        print(base / 'bin')
    elif args[0] == 'install':
        install('pymusiclooper')
elif name == 'dotnet':
    print('8.0.424 [mock SDK]')
elif name == 'node':
    print('true')
'''


class SetupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='umb-setup-test-')
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.bin = self.base / 'bin'
        self.bin.mkdir()
        self.mock = self.base / 'mock.py'
        self.mock.write_text(f'#!{sys.executable}\n' + MOCK)
        self.mock.chmod(0o755)
        (self.bin / 'dirname').symlink_to('/usr/bin/dirname')
        self.env = dict(os.environ, PATH=str(self.bin), PIPX_BIN_DIR=str(self.bin),
                        UMB_SETUP_TEST_DIR=str(self.base), UMB_SETUP_TEST_OS='Linux')
        self.add('uname', 'sudo')

    def add(self, *names):
        for name in names:
            (self.bin / name).symlink_to(self.mock)

    def calls(self):
        log = self.base / 'calls.jsonl'
        return [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []

    def run_setup(self, *args):
        return subprocess.run(['/bin/bash', str(ROOT / 'setup.sh'), *args],
                              env=self.env, cwd=self.base, text=True, capture_output=True)

    def assert_success(self, result):
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_package_managers_and_repeat_setup(self):
        for manager, system, ffmpeg_package, pipx_package in (
            ('brew', 'Darwin', 'ffmpeg', 'pipx'),
            ('apt-get', 'Linux', 'ffmpeg', 'pipx'),
            ('dnf', 'Linux', 'ffmpeg-free', 'pipx'),
            ('pacman', 'Linux', 'ffmpeg', 'python-pipx'),
        ):
            with self.subTest(manager=manager):
                self.env['UMB_SETUP_TEST_OS'] = system
                self.add(manager)
                self.assert_success(self.run_setup())
                calls = self.calls()
                installs = [c for c in calls if c[0] == manager]
                self.assertTrue(any(ffmpeg_package in c for c in installs))
                self.assertTrue(any(pipx_package in c for c in installs))
                self.assertIn(['pipx', 'install', 'pymusiclooper'], calls)
                if manager == 'apt-get':
                    self.assertEqual(installs.count(['apt-get', 'update']), 1)
                (self.base / 'calls.jsonl').unlink()
                self.assert_success(self.run_setup())
                self.assertFalse(any(c[0] in (manager, 'sudo') or c[1:2] == ['install']
                                     for c in self.calls()))
                for tool in (manager, 'ffmpeg', 'ffprobe', 'ffplay', 'pipx', 'pymusiclooper'):
                    (self.bin / tool).unlink()
                (self.base / 'calls.jsonl').unlink()

    def test_dry_run_never_invokes_installers(self):
        self.add('apt-get')
        result = self.run_setup('--dry-run')
        self.assert_success(result)
        self.assertIn('apt-get install -y ffmpeg', result.stdout)
        self.assertIn('pipx install pymusiclooper', result.stdout)
        self.assertEqual(self.calls(), [['uname', '-s']])

    def test_existing_dependencies_need_no_package_manager(self):
        self.add('ffmpeg', 'ffprobe', 'ffplay', 'pymusiclooper')
        self.assert_success(self.run_setup())
        self.assertEqual(self.calls(), [['uname', '-s']])

    def test_missing_ffprobe_installs_ffmpeg_package(self):
        self.add('apt-get', 'ffmpeg', 'ffplay', 'pymusiclooper')
        self.assert_success(self.run_setup())
        self.assertTrue(any(c[0] == 'apt-get' and 'ffmpeg' in c for c in self.calls()))

    def test_package_manager_failure_stops_setup(self):
        self.add('apt-get')
        self.env['UMB_SETUP_TEST_FAIL'] = '1'
        result = self.run_setup()
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn('Setup complete', result.stdout)
        self.assertFalse(any(c[0] == 'pipx' for c in self.calls()))

    def test_unsupported_system_is_actionable(self):
        self.env['UMB_SETUP_TEST_OS'] = 'MINGW64_NT'
        result = self.run_setup()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('setup.ps1', result.stderr)

    def test_unknown_option_changes_nothing(self):
        self.assertEqual(self.run_setup('--unexpected').returncode, 2)
        self.assertEqual(self.calls(), [])

    def test_dev_requires_sdk_before_installing_dependencies(self):
        self.add('dotnet')
        result = self.run_setup('--dev')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Node.js', result.stderr)
        self.assertFalse(any(c[0] in ('apt-get', 'brew', 'pipx') for c in self.calls()))

    def test_dev_dry_run_plans_pinned_tools_and_desktop_dependencies(self):
        self.add('apt-get', 'dotnet', 'node', 'npm', 'cargo')
        result = self.run_setup('--dev', '--dry-run')
        self.assert_success(result)
        self.assertIn('scripts/fetch-tools.sh', result.stdout)
        self.assertIn('UMB.Desktop ci', result.stdout)
        self.assertFalse(any(c[0] in ('apt-get', 'sudo', 'npm') for c in self.calls()))


if __name__ == '__main__':
    unittest.main()
