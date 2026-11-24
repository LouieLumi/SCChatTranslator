"""Bundle only the prepared application payload; no user profile is read."""
import hashlib,zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[1];payload=root/'build/payload';out=root/'build/payload.zip'
if not (payload/'SCChatUI.exe').is_file():raise SystemExit('Compile the UI first with tools/build.ps1')
forbidden={'settings.json','api.json','config.json','loginData.json','state.json','api-key.txt'}
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED,compresslevel=3) as z:
 for p in sorted(payload.rglob('*')):
  if not p.is_file() or '__pycache__' in p.parts or p.suffix=='.pyc':continue
  # Third-party packages may legitimately contain config.json; user profile files never enter staging.
  if p.parent==payload and p.name in forbidden:raise SystemExit('Private file in staging: '+p.name)
  z.write(p,p.relative_to(payload))
(root/'build/payload.sha256').write_text(hashlib.sha256(out.read_bytes()).hexdigest())
print(out, out.stat().st_size)
