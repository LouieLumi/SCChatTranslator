"""Create the original speech/star icon without third-party assets."""
import struct,zlib
from pathlib import Path

def png(size):
    rows=[]
    for y in range(size):
        row=bytearray([0])
        for x in range(size):
            a,b=x/size,y/size
            outer=(.10<a<.9 and .1<b<.73) or (.23<a<.4 and .65<b<.91 and a+b<1.2)
            inner=.16<a<.84 and .16<b<.67
            star=abs(a-.5)*2.5+abs(b-.41)*2.5<.36
            color=(120,235,207,255) if star or (outer and not inner) else ((17,28,38,255) if inner else (0,0,0,0))
            row.extend(color)
        rows.append(row)
    def chunk(k,v):return struct.pack('>I',len(v))+k+v+struct.pack('>I',zlib.crc32(k+v)&0xffffffff)
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',size,size,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(rows)))+chunk(b'IEND',b'')
parts=[(n,png(n)) for n in (16,32,48,64,128,256)]
header=struct.pack('<HHH',0,1,len(parts));offset=6+16*len(parts);body=b''
for n,data in parts:
    header+=struct.pack('<BBBBHHII',n%256,n%256,0,0,1,32,len(data),offset);body+=data;offset+=len(data)
root=Path(__file__).resolve().parents[1]
(root/'src/app.ico').write_bytes(header+body)
(root/'docs/icon.png').write_bytes(png(256))
