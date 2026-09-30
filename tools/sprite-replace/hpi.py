"""Reads TA:Kingdoms archives (.hpi, .ufo, .ccx, .kmp): HPI version 2,
as described in Joe D's HPI-FMT2. Files are zlib or LZ77 chunks,
sometimes lightly encrypted.

    python tools/sprite-replace/hpi.py <archive>     lists its files
"""
import struct
import sys
import zlib

MARKER = 0x49504148  # "HAPI"
SQSH = 0x48535153


def _lz77(src):
    out = bytearray()
    window = bytearray(4096)
    w = 1
    i = 0
    while i < len(src):
        tag = src[i]
        i += 1
        for bit in range(8):
            if not tag & (1 << bit):
                if i >= len(src):
                    return bytes(out)
                c = src[i]
                i += 1
                out.append(c)
                window[w] = c
                w = (w + 1) & 0xFFF
            else:
                if i + 1 >= len(src):
                    return bytes(out)
                n = src[i] | (src[i + 1] << 8)
                i += 2
                d = n >> 4
                if d == 0:
                    return bytes(out)
                for _ in range((n & 0x0F) + 2):
                    c = window[d]
                    out.append(c)
                    window[w] = c
                    d = (d + 1) & 0xFFF
                    w = (w + 1) & 0xFFF
    return bytes(out)


def _chunks(data, pos, size):
    """size bytes, unpacked from the SQSH chunks starting at pos."""
    out = bytearray()
    while len(out) < size:
        marker, _, method, crypt, csize, dsize = struct.unpack_from("<IBBBII", data, pos)
        if marker != SQSH:
            raise ValueError("no SQSH chunk at %d" % pos)
        body = bytearray(data[pos + 19:pos + 19 + csize])
        if crypt:
            for x in range(len(body)):
                body[x] = ((body[x] - x) ^ x) & 0xFF
        out += zlib.decompress(bytes(body)) if method == 2 else _lz77(body) if method == 1 else body
        pos += 19 + csize
        if dsize == 0:
            break
    return bytes(out[:size])


def _block(data, off, size):
    if struct.unpack_from("<I", data, off)[0] == SQSH:
        dsize = struct.unpack_from("<I", data, off + 11)[0]
        return _chunks(data, off, dsize)
    return data[off:off + size]


class Archive:
    def __init__(self, path):
        self.path = path
        self.data = open(path, "rb").read()
        marker, version = struct.unpack_from("<II", self.data, 0)
        if marker != MARKER or version != 0x00020000:
            raise ValueError("%s is not a TA:Kingdoms archive" % path)
        dirs, dsize, names, nsize, _, _ = struct.unpack_from("<6I", self.data, 8)
        self.dirs = _block(self.data, dirs, dsize)
        self.names = _block(self.data, names, nsize)
        self.files = {}
        self._walk(0, "")

    def _name(self, ptr):
        end = self.names.index(b"\0", ptr)
        return self.names[ptr:end].decode("latin-1")

    def _walk(self, at, prefix):
        name_ptr, first_dir, ndirs, first_file, nfiles = struct.unpack_from("<5I", self.dirs, at)
        for k in range(nfiles):
            np_, start, dsize, csize, _, _ = struct.unpack_from("<6I", self.dirs, first_file + 24 * k)
            self.files[(prefix + self._name(np_)).lower()] = (start, dsize, csize)
        for k in range(ndirs):
            sub = first_dir + 20 * k
            self._walk(sub, prefix + self._name(struct.unpack_from("<I", self.dirs, sub)[0]) + "/")

    def read(self, name):
        start, dsize, csize = self.files[name.lower()]
        if csize == 0:
            return self.data[start:start + dsize]
        return _chunks(self.data, start, dsize)


if __name__ == "__main__":
    a = Archive(sys.argv[1])
    for f in sorted(a.files):
        print(f, a.files[f][1])
