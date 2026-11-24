"""Read-only resolver for the installed Chinese localization's chat tokens."""
import re
from pathlib import Path

class ChatDecoder:
    def __init__(self,game_dir):
        self.path=Path(game_dir)/'data/Localization/chinese_(simplified)/global.ini'
        self.mapping={};self.stamp=None
        self.refresh()

    def refresh(self):
        try:
            stamp=self.path.stat().st_mtime_ns
            if stamp==self.stamp:return
            mapping={}
            with self.path.open(encoding='utf-8-sig') as stream:
                for line in stream:
                    key,sep,value=line.rstrip('\r\n').partition('=')
                    if sep and re.fullmatch(r'[0-9A-Za-z]{1,5}',key) and value:
                        mapping[key]=value
            self.mapping=mapping;self.stamp=stamp
        except OSError:pass

    def decode(self,value):
        marked=bool(re.search(r'\[zh\]',value,re.I))
        def expand(match):
            return self.mapping.get(match.group(1),match.group(0))
        if marked:
            # Players may type a prefix before the localization marker (w[zh]).
            # Preserve that text and ordinary Latin words; resolve only known keys.
            body=re.sub(r'\[zh\]','',value,flags=re.I).lstrip()
            return re.sub(r'@([A-Za-z0-9]+)',expand,body),True
        def expand_run(match):
            keys=re.findall(r'@([A-Za-z0-9]+)',match.group(0))
            # Without a marker, require a run of known keys, not an @mention/email.
            if all(key in self.mapping for key in keys):
                return re.sub(r'@([A-Za-z0-9]+)',expand,match.group(0))
            return match.group(0)
        decoded=re.sub(r'(?<![\w@])(?:@[A-Za-z0-9]+){2,}(?![\w@])',expand_run,value)
        return decoded,decoded!=value or bool(re.search(r'[\u3400-\u9fff]',value))

    def encode(self,value):
        """Encode Chinese with the installed font's own tokens; never guess unknown glyphs."""
        self.refresh()
        reverse={}
        for key,char in sorted(self.mapping.items(),key=lambda pair:(len(pair[0]),pair[0])):
            if len(char)==1:reverse.setdefault(char,key)
        missing=sorted({c for c in value if '\u3400'<=c<='\u9fff' and c not in reverse})
        if missing:raise ValueError('本机汉化字库缺少：'+''.join(missing[:12]))
        if not re.search(r'[\u3400-\u9fff]',value):return value
        out=[];token=False
        for char in value:
            if ord(char)>127 and char in reverse:
                out.append('@'+reverse[char]);token=True
            else:
                if token and char.isascii() and char.isalnum():out.append(' ')
                out.append(char);token=False
        return '[zh] '+''.join(out)
