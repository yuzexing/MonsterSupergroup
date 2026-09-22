import ctypes as C
from ctypes import wintypes as W
import struct, pathlib, sys, argparse
parser = argparse.ArgumentParser(description='Offline x64 Unity minidump analysis. Raw stack candidates are NOT unwound frames.')
parser.add_argument('dump')
parser.add_argument('--unity-symbols', required=True)
parser.add_argument('--symbol-cache', required=True)
parser.add_argument('--all-threads', action='store_true')
args = parser.parse_args()
sys.stdout.reconfigure(encoding='utf-8')

data = pathlib.Path(args.dump).read_bytes()
u32 = lambda p: struct.unpack_from('<I', data, p)[0]
u64 = lambda p: struct.unpack_from('<Q', data, p)[0]
streams = {u32(u32(12) + n*12): (u32(u32(12) + n*12+8), u32(u32(12) + n*12+4)) for n in range(u32(8))}
mods = []
identities = {}
p = streams[4][0]
for i in range(u32(p)):
    m = p+4+i*108
    name = u32(m+20)
    path = data[name+4:name+4+u32(name)].decode('utf-16-le')
    mods.append((u64(m), u32(m+8), path))
    identities[path] = (u32(m+8), u32(m+16))
ranges = []
if 9 in streams:
    p = streams[9][0]; filepos = u64(p+8)
    for i in range(u64(p)):
        m = p+16+i*16; start, size = u64(m), u64(m+8)
        ranges.append((start, size, filepos)); filepos += size
if 5 in streams:
    p = streams[5][0]
    for i in range(u32(p)):
        m = p+4+i*16; ranges.append((u64(m), u32(m+8), u32(m+12)))
p = streams[3][0]
for i in range(u32(p)):
    m=p+4+i*48; ranges.append((u64(m+24),u32(m+32),u32(m+36)))
def module(addr):
    for base,size,name in mods:
        if base <= addr < base+size: return pathlib.Path(name).name, addr-base
    return '?', addr
p = streams[6][0]
print('thread',u32(p),'exception',hex(u32(p+8)),'address',hex(u64(p+24)),module(u64(p+24)))
print('exception parameters', [hex(u64(p+40+i*8)) for i in range(min(15,u32(p+32)))])
fault=u64(p+48)
if 16 in streams:
    at=streams[16][0];header=u32(at);stride=u32(at+4)
    for n in range(u64(at+8)):
        m=at+header+n*stride
        if u64(m)<=fault<u64(m)+u64(m+24):
            print('fault memory region base/size/state/protect/type',hex(u64(m)),hex(u64(m+24)),hex(u32(m+32)),hex(u32(m+36)),hex(u32(m+40)))
ctx = C.create_string_buffer(data[u32(p+164):u32(p+164)+u32(p+160)])
rsp=struct.unpack_from('<Q',ctx.raw,152)[0]; rip=struct.unpack_from('<Q',ctx.raw,248)[0]
print('RIP',hex(rip),'RSP',hex(rsp))

dbgdir='C:/Program Files (x86)/Windows Kits/10/Debuggers/x64'
dll = C.WinDLL(dbgdir+'/dbghelp.dll')
process = C.c_void_p(-1)
dll.SymSetOptions.argtypes=[W.DWORD]; dll.SymSetOptions(0x80000000|0x10|0x4|0x200)
dll.SymInitializeW.argtypes=[C.c_void_p,W.LPCWSTR,W.BOOL]; dll.SymInitializeW.restype=W.BOOL
search=args.unity_symbols + ';srv*' + args.symbol_cache + '*https://symbolserver.unity3d.com/;srv*' + args.symbol_cache + '*https://msdl.microsoft.com/download/symbols'
print('symbols initialized',dll.SymInitializeW(process,search,False),flush=True)
dll.SymLoadModuleExW.argtypes=[C.c_void_p,C.c_void_p,W.LPCWSTR,W.LPCWSTR,C.c_ulonglong,W.DWORD,C.c_void_p,W.DWORD]; dll.SymLoadModuleExW.restype=C.c_ulonglong
images={}
def image_sections(name):
    if name not in images:
        try:
            raw=pathlib.Path(name).read_bytes(); pe=struct.unpack_from('<I',raw,60)[0]
            identity=(struct.unpack_from('<I',raw,pe+24+56)[0],struct.unpack_from('<I',raw,pe+8)[0])
            if raw[pe:pe+4] != b'PE\0\0' or identity != identities[name]:
                print('Image identity mismatch; no symbols or image fallback:',name,flush=True)
                images[name]=(b'',[])
                return images[name]
            count=struct.unpack_from('<H',raw,pe+6)[0]; opt=struct.unpack_from('<H',raw,pe+20)[0]
            sections=[]
            for i in range(count):
                at=pe+24+opt+i*40
                virtualsize,virtualaddr,rawsize,rawaddr=struct.unpack_from('<IIII',raw,at+8)
                flags=struct.unpack_from('<I',raw,at+36)[0]
                sections.append((virtualaddr,rawsize,rawaddr,flags))
            images[name]=(raw,sections)
        except (OSError,struct.error): images[name]=(b'',[])
    return images[name]

for base,size,name in mods:
    if not image_sections(name)[0]: continue
    loaded=dll.SymLoadModuleExW(process,None,name,None,base,size,None,0)
    if pathlib.Path(name).name in ['UnityPlayer.dll','D3D12Core.dll']:
        print('loaded identity-matched image',name,hex(loaded),flush=True)

def executable_address(address):
    for base,length,name in mods:
        if base <= address < base+length:
            _, sections=image_sections(name)
            return any(start <= address-base < start+size and flags & 0x20000000 for start,size,_,flags in sections)
    return False
READ=C.WINFUNCTYPE(W.BOOL,C.c_void_p,C.c_ulonglong,C.c_void_p,W.DWORD,C.POINTER(W.DWORD))
@READ
def read_mem(proc,addr,out,size,read):
    for start,length,pos in ranges:
        if start <= addr and addr+size <= start+length:
            C.memmove(out,data[pos+addr-start:pos+addr-start+size],size);read[0]=size;return True
    for base,length,name in mods:
        if base <= addr and addr+size <= base+length:
            raw,sections=image_sections(name);rva=addr-base
            for start,sizeonfile,pos,flags in sections:
                if start <= rva and rva+size <= start+sizeonfile:
                    C.memmove(out,raw[pos+rva-start:pos+rva-start+size],size);read[0]=size;return True
    read[0]=0;return False
dll.SymFunctionTableAccess64.argtypes=[C.c_void_p,C.c_ulonglong]; dll.SymFunctionTableAccess64.restype=C.c_void_p
dll.SymGetModuleBase64.argtypes=[C.c_void_p,C.c_ulonglong]; dll.SymGetModuleBase64.restype=C.c_ulonglong
dll.StackWalk64.argtypes=[W.DWORD,C.c_void_p,C.c_void_p,C.c_void_p,C.c_void_p,READ,C.c_void_p,C.c_void_p,C.c_void_p];dll.StackWalk64.restype=W.BOOL
dll.SymFromAddr.argtypes=[C.c_void_p,C.c_ulonglong,C.POINTER(C.c_ulonglong),C.c_void_p];dll.SymFromAddr.restype=W.BOOL
def describe(addr):
    info=C.create_string_buffer(4096);struct.pack_into('<I',info,0,88);struct.pack_into('<I',info,80,4000)
    delta=C.c_ulonglong()
    if dll.SymFromAddr(process,addr,C.byref(delta),info):
        return f'{module(addr)[0]}!{info.raw[84:].split(bytes([0]))[0].decode(errors="replace")}+0x{delta.value:x}'
    return '%s+0x%x'%module(addr)
stack=C.create_string_buffer(4096)
for offset,value in [(0,rip),(32,struct.unpack_from('<Q',ctx.raw,160)[0]),(48,rsp)]:
    struct.pack_into('<Q',stack,offset,value);struct.pack_into('<I',stack,offset+12,3)
print('exception frame',describe(rip),flush=True)
for i in range(45):
    ok=dll.StackWalk64(0x8664,process,None,stack,ctx,read_mem,dll.SymFunctionTableAccess64,dll.SymGetModuleBase64,None)
    if not ok: print('stackwalk stopped',C.GetLastError());break
    addr=struct.unpack_from('<Q',stack.raw,0)[0]
    if not addr:break
    if not executable_address(addr):
        print('Unwind incomplete: next address is not verified executable code; do not treat further stack words as frames.');break
    print(i,hex(addr),describe(addr),flush=True)
print('Raw stack address candidates (not an unwound call stack):',flush=True)
seen=set()
cache_return=None
for delta in range(0,4096,8):
    word=C.c_ulonglong();count=W.DWORD()
    if read_mem(process,rsp+delta,C.addressof(word),8,C.pointer(count)) and module(word.value)[0] in ['UnityPlayer.dll','D3D12Core.dll','AppUINativePlugin.dll','mono-2.0-bdwgc.dll','d3d12.dll'] and word.value not in seen:
        seen.add(word.value);name=describe(word.value);print(hex(delta),name,flush=True)
        if 'SaveCache@D3D12PipelineCacheLibrary' in name:cache_return=(delta,word.value)
if cache_return:
    delta,addr=cache_return
    op=C.create_string_buffer(8);read=W.DWORD();read_mem(process,addr-8,C.addressof(op),8,C.pointer(read))
    print('SaveCache return callsite preceding bytes',op.raw.hex(),flush=True)
    # D3D12Core unwind failed. Resume at the identified caller return address, explicitly separated from the raw scan.
    p=streams[6][0];ctx=C.create_string_buffer(data[u32(p+164):u32(p+164)+u32(p+160)])
    struct.pack_into('<Q',ctx,248,addr);struct.pack_into('<Q',ctx,152,rsp+delta+8)
    stack=C.create_string_buffer(4096)
    for offset,value in [(0,addr),(32,struct.unpack_from('<Q',ctx.raw,160)[0]),(48,rsp+delta+8)]:
        struct.pack_into('<Q',stack,offset,value);struct.pack_into('<I',stack,offset+12,3)
    print('Caller unwind resumed at SaveCache return address:',flush=True)
    for i in range(20):
        if not dll.StackWalk64(0x8664,process,None,stack,ctx,read_mem,dll.SymFunctionTableAccess64,dll.SymGetModuleBase64,None):break
        address=struct.unpack_from('<Q',stack.raw,0)[0]
        if not address:break
        if not executable_address(address):
            print('Resumed unwind incomplete: next address is not verified executable code.');break
        print(i,describe(address),flush=True)

if args.all_threads:
    print('Other thread instruction pointers and code-address candidates (NOT call stacks):',flush=True)
    at=streams[3][0]
    for index in range(u32(at)):
        entry=at+4+index*48; thread_id=u32(entry)
        context=data[u32(entry+44):u32(entry+44)+u32(entry+40)]
        if len(context)<256: continue
        sp=struct.unpack_from('<Q',context,152)[0]; ip=struct.unpack_from('<Q',context,248)[0]
        print('thread',thread_id,'IP',describe(ip),flush=True)
        seen=set(); found=0
        for offset in range(0,2048,8):
            word=C.c_ulonglong();read=W.DWORD()
            if read_mem(process,sp+offset,C.addressof(word),8,C.pointer(read)) and executable_address(word.value) and word.value not in seen:
                seen.add(word.value)
                name=describe(word.value)
                if any(key in name for key in ['Gfx','Render','Present','WaitFor','PlayerLoop','RunTask','SaveCache']):
                    print('  candidate',hex(offset),name,flush=True);found+=1
                    if found>=8:break
dll.SymCleanup.argtypes=[C.c_void_p]
dll.SymCleanup(process)

