"""Validate every CUR directory, PNG frame and RIFF boundary without Pillow."""
import struct
import unittest
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def validate_cur(data):
    reserved, kind, count = struct.unpack_from('<HHH', data)
    assert (reserved, kind, count) == (0, 2, 3)
    sizes = []
    for i in range(count):
        w, h, _, _, x, y, length, offset = struct.unpack_from('<BBBBHHII', data, 6 + 16*i)
        assert w == h and x < w and y < h
        assert offset >= 6 + 16*count and offset + length <= len(data)
        png = data[offset:offset+length]
        assert png[:8] == b'\x89PNG\r\n\x1a\n'
        assert struct.unpack_from('>II', png, 16) == (w, h)
        position = 8
        while position < len(png):
            n, = struct.unpack_from('>I', png, position)
            chunk = png[position+4:position+8+n]
            crc, = struct.unpack_from('>I', png, position+8+n)
            assert zlib.crc32(chunk) & 0xffffffff == crc
            position += n + 12
        assert position == len(png)
        sizes.append(w)
    assert sorted(sizes) == [32, 48, 64]


def chunks(data, start, end):
    while start < end:
        tag, n = struct.unpack_from('<4sI', data, start)
        start += 8
        assert start+n <= end
        yield tag, data[start:start+n]
        start += n + n % 2
    assert start == end


class Assets(unittest.TestCase):
    def test_static_cursors(self):
        for path in (ROOT/'cursor-pack').glob('*.cur'):
            with self.subTest(path=path.name):
                validate_cur(path.read_bytes())

    def test_animated_cursors(self):
        for path in (ROOT/'cursor-pack').glob('*.ani'):
            with self.subTest(path=path.name):
                data = path.read_bytes()
                self.assertEqual(data[:4], b'RIFF')
                self.assertEqual(data[8:12], b'ACON')
                self.assertEqual(struct.unpack_from('<I', data, 4)[0]+8, len(data))
                records = dict(chunks(data, 12, len(data)))
                cb, frames, steps, w, h, bpp, planes, rate, flags = struct.unpack('<9I', records[b'anih'])
                self.assertEqual((cb, frames, steps, w, h, bpp, planes, flags), (36, 10, 10, 0, 0, 32, 1, 1))
                self.assertGreater(rate, 0)
                self.assertEqual(records[b'LIST'][:4], b'fram')
                icons = list(chunks(records[b'LIST'], 4, len(records[b'LIST'])))
                self.assertEqual(len(icons), frames)
                self.assertEqual(len({frame for _, frame in icons}), frames)
                for tag, frame in icons:
                    self.assertEqual(tag, b'icon')
                    validate_cur(frame)


if __name__ == '__main__':
    unittest.main()
