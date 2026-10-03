"""Exercise repeat-run verification without downloading or installing anything."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class FetchToolsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'scripts').mkdir()
        self.script = self.root / 'scripts/fetch-tools.sh'
        shutil.copyfile(Path(__file__).with_name('fetch-tools.sh'), self.script)
        self.fake_bin = self.root / 'bin'
        self.fake_bin.mkdir()
        # Architecture queries need x86_64, while OS queries need Linux.
        self.executable(self.fake_bin / 'uname', '[ "$1" = -m ] && echo x86_64 || echo Linux')
        self.executable(self.fake_bin / 'curl', 'echo unexpected-download >&2; exit 47')
        for command in ['ffmpeg', 'ffprobe', 'ffplay', 'pymusiclooper']:
            self.executable(self.fake_bin / command, 'exit 0')
        self.tools = [self.root / 'Tools' / folder / name for folder, name in [
            ('Nus3Audio', 'nus3audio'), ('UltimateTexCli', 'ultimate_tex_cli'),
            ('BgmProperty', 'bgm-property'), ('vgmstream-cli', 'vgmstream-cli')]]
        for tool in self.tools:
            self.executable(tool, 'exit 0')
        self.executable(self.tools[-1], 'echo \'{"version":"r2083","extensions":[]}\'; exit 1')

    @staticmethod
    def executable(path, body):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('#!/bin/sh\n' + body + '\n')
        path.chmod(0o755)

    def run_fetch(self):
        env = dict(os.environ, PATH=f'{self.fake_bin}:/usr/bin:/bin')
        return subprocess.run(['bash', str(self.script)], env=env, text=True, capture_output=True)

    def test_working_existing_tools_are_probed_without_downloads(self):
        result = self.run_fetch()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.count(' OK'), 4)
        self.assertNotIn('unexpected-download', result.stderr)

    def test_broken_existing_binary_triggers_repair_and_download_failure_is_fatal(self):
        self.executable(self.tools[0], 'exit 134')
        result = self.run_fetch()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('cannot start', result.stderr)
        self.assertIn('Reinstalling unusable binary', result.stdout)
        self.assertIn('unexpected-download', result.stderr)

    def test_vgmstream_exit_one_without_version_json_is_not_accepted(self):
        self.executable(self.tools[-1], 'exit 1')
        result = self.run_fetch()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('vgmstream-cli cannot start', result.stderr)
        self.assertIn('unexpected-download', result.stderr)


if __name__ == '__main__':
    unittest.main()
