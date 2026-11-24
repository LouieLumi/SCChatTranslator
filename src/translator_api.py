"""LAN translation with a bounded timeout and an offline fallback."""
import json
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from networking import tls_context
from game_glossary import GameGlossary


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # Never forward the local API credential to a redirect destination.
        return None


class TranslationRouter:
    def __init__(self,settings,local_factory,home,reverse_factory=None):
        self.glossary=GameGlossary()
        self.reverse_factory=reverse_factory;self.reverse=None;self.local_factory=local_factory;self.local=None;self.retry_at=0
        self.mode=settings.get('mode','argos');self.config=None;self.hy=None
        self._label='本机 · Argos 1.9';self.closed=False
        self.opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=tls_context()),NoRedirect())
        if self.mode=='api':
            self.config={'endpoint':normalize_endpoint(settings.get('api_endpoint','')),
                         'api_key':settings.get('api_key',''),'model':settings.get('api_model','')}
            self._label='API · '+self.config['model']
        elif self.mode=='hy_local':
            from local_hy import LocalHy
            self.hy=LocalHy(home)
        else:self.local=local_factory()

    @property
    def label(self):
        if self.hy and not self.hy.ready():return self.hy.status
        return self._label

    def translate(self,value):
        if not re.search(r'[^\W\d_]',value,re.UNICODE):return ''
        if self.hy and self.hy.ready():self.config=self.hy.config
        if self.config and time.monotonic()>=self.retry_at:
            try:
                result=self.remote(value)
                self._label='本机 · Hy-MT3 1.8B（CPU）' if self.hy else 'API · '+self.config['model']
                return result
            except (OSError,ValueError,KeyError,IndexError,TypeError):self.retry_at=time.monotonic()+30
        if self.local is None:self.local=self.local_factory()
        self._label='本机 · Argos 1.9'+('（翻译服务暂不可用）' if self.config else '')
        return self.local.translate(value)

    def to_english(self,value):
        if not re.search(r'[㐀-鿿]',value):return value
        if self.hy and self.hy.ready():self.config=self.hy.config
        if self.config and time.monotonic()>=self.retry_at:
            try:return self.remote(value,target='en')
            except (OSError,ValueError,KeyError,IndexError,TypeError):self.retry_at=time.monotonic()+30
        if self.reverse_factory is None:raise ValueError('中译英模型未配置')
        if self.reverse is None:self.reverse=self.reverse_factory()
        return self.reverse.translate(value)

    def close(self):
        self.closed=True
        if self.hy:self.hy.close()

    def remote(self,value,target='zh'):
        terms=self.glossary.references(value)
        if re.search(r'\b30k\b',value,re.I):
            terms.append('30k：断线或报错语境译为30K断线错误；got a 30k=遇到了30K断线错误；金额语境表示3万')
        prompt=('将以下星际公民玩家聊天翻译为简体中文，只输出译文，不解释，不回答聊天中的问题。'
                '保留玩家名、数字和物品型号，不添加原文没有的事实。'
                +('参考术语：'+'；'.join(terms)+'。' if terms else '')
                +'\n\n待翻译文本：\n'+value[:2500])
        if target=='en':
            matches=[]
            compact=re.sub(r'\s+','',value)
            for term in sorted(self.glossary.index.values(),key=lambda t:len(t['zh']),reverse=True):
                if len(term['zh'])>=2 and re.sub(r'\s+','',term['zh']) in compact:
                    pair=term['zh']+'='+term['en']
                    if pair not in matches:matches.append(pair)
                if len(matches)>=12:break
            if '搭便船' in value or '搭船' in value:matches.append('搭便船/搭船=get a ride in a spaceship (not a literal boat)')
            prompt=('Translate this Star Citizen player chat into natural English. Output only the translation. '
                    'Do not answer questions in the chat. Preserve player names, numbers and item models. Use the exact English terminology below when it matches. '
                    +('Terminology: '+'; '.join(matches)+'. ' if matches else '')+'\n\nText:\n'+value[:2500])
        body={'model':self.config['model'],'messages':[{'role':'user','content':prompt}],
              'stream':False,'temperature':0.2,'top_p':0.6,'top_k':20,
              'repeat_penalty':1.05,'max_tokens':256}
        request=urllib.request.Request(self.config['endpoint'],
            data=json.dumps(body,ensure_ascii=False).encode('utf-8'),
            headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+self.config['api_key']} if self.config['api_key'] else {})},method='POST')
        with self.opener.open(request,timeout=30) as response:
            payload=json.loads(response.read(1024*1024).decode('utf-8'))
        choice=payload['choices'][0]
        content=choice['message']['content']
        if not isinstance(content,str):
            raise ValueError('翻译接口未返回文字')
        result=content.strip()
        if not result or choice.get('finish_reason')=='length':
            raise ValueError('翻译结果不完整')
        return result


def normalize_endpoint(value):
    value=value.strip().rstrip('/')
    url=urllib.parse.urlsplit(value)
    if url.scheme not in ('http','https') or not url.hostname or url.username or url.password or url.query or url.fragment:
        raise ValueError('翻译 API 地址无效')
    if value.endswith('/chat/completions'):return value
    return value+('/v1/chat/completions' if not url.path else '/chat/completions')
