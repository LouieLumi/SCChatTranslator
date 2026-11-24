"""Bounded, whole-term lookup in the attributed Star Citizen glossary."""
import json
import re
from pathlib import Path

class GameGlossary:
    # These ordinary words need ship/weapon context when typed in lowercase.
    AMBIGUOUS={'arrow','blade','defender','dragonfly','eclipse','fury','gladiator','guardian','hawk','herald','hornet','hurricane','hull','mantis','mustang','nomad','origin','prospector','pulse','ranger','razor','reclaimer','redeemer','reliant','spirit','star','storm','titan','turtle','valkyrie','vanguard','vulture','zeus'}
    IGNORE={'a','an','and','are','as','at','be','by','can','do','for','from','go','he','her','here','i','in','is','it','me','my','no','not','of','on','or','our','out','she','so','that','the','their','there','this','to','up','us','we','what','with','you','your','red','black','blue','green','white','new','old','left','right','yes'}
    def __init__(self,path=None):
        path=Path(path or Path(__file__).with_name('glossary.json'))
        self.index={};self.count=0;self.literal_cache={}
        try:
            data=json.loads(path.read_text(encoding='utf-8'))
            self.count=data['count']
            for term in data['terms']:
                names=[term['en']]+[v for v in term.get('variants',[]) if isinstance(v,str)]
                for name in names:
                    name=' '.join(name.split());key=name.casefold()
                    if len(name)<2 or key in self.IGNORE or '\n' in term['zh']:continue
                    self.index.setdefault(key,{'en':name,'zh':term['zh'],'case_sensitive':term.get('case_sensitive',False) or len(name)<=3})
        except (OSError,ValueError,KeyError):pass
        # Common chat spelling; the canonical translation comes from the glossary.
        for alias,target in [('hanger','hangar'),('hangers','hangar'),('crime stats','crimestat'),('crime stat','crimestat')]:
            if target in self.index:self.index[alias]={'en':alias,'zh':self.index[target]['zh'],'case_sensitive':False}
        patterns=[re.escape(x).replace(r'\ ',r'\s+') for x in sorted(self.index,key=len,reverse=True)]
        self.pattern=re.compile(r'(?<![A-Za-z0-9_@])(?:'+'|'.join(patterns)+r')(?![A-Za-z0-9_])',re.I) if patterns else None

    def matches(self,value,limit=12):
        if not self.pattern:return []
        protected=[m.span() for m in re.finditer(r'https?://\S+|www\.\S+|[\w.+-]+@[\w.-]+\.[A-Za-z]+',value)]
        ship_context=bool(re.search(r'\b(ship|ships|fly|flying|pilot|hangar|hangars|rent|rented|bought|weapon|weapons|fighter|landing|landed|aboard|quantum|turret|cargo)\b',value,re.I))
        result=[]
        for match in self.pattern.finditer(value):
            key=' '.join(match.group().split()).casefold();term=self.index[key]
            if term['case_sensitive'] and match.group()!=term['en']:continue
            if key in self.AMBIGUOUS and match.group().islower() and not ship_context:continue
            if any(a<match.end() and b>match.start() for a,b in protected):continue
            result.append((match.start(),match.end(),term))
            if len(result)>=limit:break
        return result

    def references(self,value):
        seen=set();out=[]
        for _,_,term in self.matches(value):
            pair=term['en']+'='+term['zh']
            if pair not in seen:seen.add(pair);out.append(pair)
        return out

    def translate_with_terms(self,value,translate):
        # Argos cannot consume a prompted glossary. Keep whole-sentence grammar,
        # then canonicalize retained English or an unambiguous literal rendering.
        result=translate(value)
        for _,_,term in self.matches(value,limit=8):
            if term['zh'] in result:continue
            name=term['en']
            result=re.sub(r'(?<![A-Za-z0-9_])'+re.escape(name)+r'(?![A-Za-z0-9_])',lambda _:term['zh'],result,flags=re.I)
            if term['zh'] in result:continue
            if name not in self.literal_cache:self.literal_cache[name]=translate(name).strip()
            literal=self.literal_cache[name]
            if 3<=len(literal)<=35 and not re.search(r'[\ue000-\uf8ff\u2047\ufffd]',literal):
                result=result.replace(literal,term['zh'])
        return result
