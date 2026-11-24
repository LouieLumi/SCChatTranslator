"""Optional, user-selected local Hy model; owns only its own server process."""
import hashlib
import json
import os
import secrets
import socket
import subprocess
import threading
import time
import urllib.request
import zipfile
from pathlib import Path
from networking import tls_context

MODEL_URL='https://huggingface.co/tencent/Hy-MT3-1.8B-GGUF/resolve/4c4370b31f13ccac4ac71133e3a0967c88cc792c/Hy-MT3-1.8B-Q4_K_M.gguf?download=true'
MODEL_SHA='bae6e591103654ff43ef0de7d1492f6e6df1f955505f6bb2cc52e86fc399a942'
MODEL_SIZE=1348432160
RUNTIME_URL='https://github.com/ggml-org/llama.cpp/releases/download/b12676/llama-b12676-bin-win-cpu-x64.zip'
RUNTIME_SHA='fdf78a3924fe92ccfb24af58544a6dccbc1feeb0587e77fca9ec3bef6737086c'
RUNTIME_SIZE=25216675


class LocalHy:
    def __init__(self,home):
        self.root=Path(home)/'hy';self.root.mkdir(parents=True,exist_ok=True)
        self.stop=threading.Event();self.lock=threading.Lock();self.process=None
        self.config=None;self.status='正在准备本机 Hy 模型';self.key_file=None
        self.worker=threading.Thread(target=self.prepare,daemon=True);self.worker.start()

    def check(self):
        if self.stop.is_set():raise InterruptedError('已取消')

    def sha(self,path):
        h=hashlib.sha256()
        with path.open('rb') as f:
            for chunk in iter(lambda:f.read(4*1024*1024),b''):
                self.check();h.update(chunk)
        return h.hexdigest()

    def download(self,url,path,expected,size,label):
        marker=path.with_suffix(path.suffix+'.verified.json')
        if path.exists() and path.stat().st_size==size:
            stat=path.stat();identity={'sha':expected,'size':size,'mtime':stat.st_mtime_ns}
            try:
                if json.loads(marker.read_text())==identity:return path
            except (OSError,ValueError):pass
            self.status='正在校验'+label
            if self.sha(path)==expected:
                marker.write_text(json.dumps(identity));return path
        part=path.with_suffix(path.suffix+'.part')
        start=part.stat().st_size if part.exists() else 0
        if start>=size:part.unlink();start=0
        self.status='正在下载'+label
        request=urllib.request.Request(url,headers={'Range':'bytes='+str(start)+'-'} if start else {})
        opener=urllib.request.build_opener(urllib.request.HTTPSHandler(context=tls_context()))
        with opener.open(request,timeout=20) as response:
            resume=start>0 and response.status==206 and response.headers.get('Content-Range','').startswith('bytes '+str(start)+'-')
            if response.status==206 and not resume and start:raise ValueError('下载续传响应无效')
            done=start if resume else 0
            with part.open('ab' if resume else 'wb') as f:
                while True:
                    self.check();chunk=response.read(1024*1024)
                    if not chunk:break
                    done+=len(chunk)
                    if done>size:raise ValueError('下载文件长度异常')
                    f.write(chunk);self.status='下载'+label+' '+str(int(done*100/size))+'%'
        self.check();self.status='正在校验'+label
        if part.stat().st_size!=size or self.sha(part)!=expected:
            part.unlink(missing_ok=True);raise ValueError('下载文件校验失败')
        os.replace(part,path)
        marker.write_text(json.dumps({'sha':expected,'size':size,'mtime':path.stat().st_mtime_ns}))
        return path

    def prepare(self):
        try:
            model=self.download(MODEL_URL,self.root/'Hy-MT3-1.8B-Q4_K_M.gguf',MODEL_SHA,MODEL_SIZE,'Hy 模型')
            archive=self.download(RUNTIME_URL,self.root/'llama-b12676-win-cpu.zip',RUNTIME_SHA,RUNTIME_SIZE,'推理组件')
            runtime=self.root/'runtime-b12676';ready=runtime/'.ready'
            if not ready.exists():
                self.status='正在准备推理组件';runtime.mkdir(exist_ok=True)
                with zipfile.ZipFile(archive) as z:
                    for item in z.infolist():
                        self.check();target=(runtime/item.filename).resolve()
                        if not target.is_relative_to(runtime.resolve()):raise ValueError('压缩包路径无效')
                        z.extract(item,runtime)
                ready.write_text(RUNTIME_SHA)
            server=next(runtime.rglob('llama-server.exe'))
            with socket.socket() as sock:
                sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
            key=secrets.token_urlsafe(32);self.key_file=self.root/('key-'+str(os.getpid())+'.txt');self.key_file.write_text(key+'\n')
            self.status='正在启动本机 Hy（CPU）'
            args=[str(server),'-m',str(model),'--host','127.0.0.1','--port',str(port),'--alias','Hy-MT3-1.8B','--api-key-file',str(self.key_file),'--no-webui','--ctx-size','2048','--parallel','1','--threads','2','--threads-batch','2','--n-gpu-layers','0','--log-verbosity','2']
            with self.lock:
                self.check();self.process=subprocess.Popen(args,cwd=server.parent,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));address='http://127.0.0.1:'+str(port)
            deadline=time.monotonic()+90
            while time.monotonic()<deadline:
                self.check()
                if self.process.poll() is not None:raise RuntimeError('Hy 进程启动失败')
                try:
                    with opener.open(address+'/health',timeout=1) as r:
                        if r.status==200:
                            self.config={'endpoint':address+'/v1/chat/completions','api_key':key,'model':'Hy-MT3-1.8B'}
                            self.status='本机 · Hy-MT3 1.8B（CPU）';return
                except OSError:pass
                self.stop.wait(.3)
            raise TimeoutError('Hy 启动超时')
        except InterruptedError:pass
        except Exception as e:
            self.status='Hy 准备失败，暂用 Argos；保存设置可重试（'+type(e).__name__+'）'
            self.terminate()

    def ready(self):
        return self.config is not None and self.process is not None and self.process.poll() is None

    def terminate(self):
        with self.lock:
            if self.process and self.process.poll() is None:
                self.process.terminate()
                try:self.process.wait(2)
                except subprocess.TimeoutExpired:self.process.kill();self.process.wait()
        if self.key_file:self.key_file.unlink(missing_ok=True)

    def close(self):
        self.stop.set();self.terminate();self.worker.join(timeout=.2)
