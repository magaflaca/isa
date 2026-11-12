import struct, sys
p = sys.argv[1]
d = bytearray(open(p, 'rb').read())
pe = struct.unpack_from('<I', d, 0x3c)[0]
off = pe + 4 + 18
chars = struct.unpack_from('<H', d, off)[0]
struct.pack_into('<H', d, off, chars | 0x20)
open(p, 'wb').write(d)
print('LARGEADDRESSAWARE set on', p)
