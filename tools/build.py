"""Build the x64 WPF UI and self-extracting EXE on Windows; no policy changes."""
import hashlib,os,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
if os.name!='nt':raise SystemExit('Run this build step on Windows x64')
f=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319'
src=root/'src';build=root/'build';payload=build/'payload';dist=root/'dist';dist.mkdir(exist_ok=True)
if not (payload/'runtime/python.exe').is_file():raise SystemExit('Run python tools/prepare.py first')
shared=[str(f/'csc.exe'),'/nologo','/codepage:65001','/target:winexe','/platform:x64','/optimize+',f'/win32manifest:{src / "app.manifest"}',f'/win32icon:{src / "app.ico"}','/reference:System.Xaml.dll']
shared += ['/reference:'+str(f/'WPF'/name) for name in ('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')]
subprocess.run(shared+['/out:'+str(payload/'SCChatUI.exe'),'/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll']+[str(src/name) for name in ('App.cs','SettingsWindow.cs','ChildJob.cs','GameHotkeys.cs','ChatVisibilityPrompt.cs')],check=True)
subprocess.run([sys.executable,str(root/'tools/package.py')],check=True)
subprocess.run(shared+['/out:'+str(dist/'SCChatTranslator.exe'),'/reference:System.IO.Compression.dll',f'/resource:{build / "payload.zip"},SCChat.Payload',f'/resource:{build / "payload.sha256"},SCChat.Hash',str(src/'Bootstrap.cs')],check=True)
hash_value=hashlib.sha256((dist/'SCChatTranslator.exe').read_bytes()).hexdigest()
(dist/'SHA256SUMS.txt').write_text(hash_value+'  SCChatTranslator.exe\n')
print('Built',dist/'SCChatTranslator.exe')
