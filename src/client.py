"""Standalone Star Citizen public server chat client. Sends only explicit user requests."""
import base64
import json
import re
import time
from pathlib import Path
from urllib.parse import urlsplit
import grpc

METHODS = {
 'identity':'/sc.external.services.identity.v1.IdentityService/GetCurrentPlayer',
 'groups':'/sc.external.services.social.v1.SocialService/ListGroups',
 'messages':'/sc.external.services.chat.v1.ChatService/ListMessages',
 'send':'/sc.external.services.chat.v1.ChatService/SendMessage',
}

def varint(n):
    if n < 0: raise ValueError('negative varint')
    out=bytearray()
    while n>127: out.append((n&127)|128); n>>=7
    out.append(n)
    return bytes(out)

def blob(field,value):
    return varint(field*8+2)+varint(len(value))+value

def fields(data):
    def read_int(pos):
        value=0
        for i in range(10):
            if pos>=len(data): raise ValueError('truncated varint')
            b=data[pos];pos+=1;value|=(b&127)<<(7*i)
            if b<128:return value,pos
        raise ValueError('invalid varint')
    pos=0;result={}
    while pos<len(data):
        tag,pos=read_int(pos);field,wire=tag>>3,tag&7
        if not field:raise ValueError('invalid field')
        if wire==0:value,pos=read_int(pos)
        elif wire in (1,2,5):
            if wire==2:count,pos=read_int(pos)
            else:count=8 if wire==1 else 4
            if count>len(data)-pos:raise ValueError('truncated field')
            value=data[pos:pos+count];pos+=count
        else:raise ValueError('unsupported wire type')
        result.setdefault(field,[]).append(value)
    return result

def text_field(data,field):
    return data.get(field,[b''])[0].decode('utf-8',errors='replace')

def validate_endpoint(value):
    u=urlsplit(value if '://' in value else 'https://'+value)
    if (u.scheme!='https' or not re.fullmatch(r'pub-sc-[a-z0-9-]+\.test1\.cloudimperiumgames\.com',u.hostname or '')
        or u.port not in (443,None) or u.username or u.password or u.path not in ('','/') or u.query or u.fragment):
        raise ValueError('游戏服务域名不在允许范围，未发送登录信息')
    return u.hostname+':443'

class ChatClient:
    def __init__(self,game_dir):
        self.game_dir=Path(game_dir);self.channel=None;self.metadata=None;self.refresh_at=0
        self.endpoint=None;self.login_stamp=None

    def close(self):
        if self.channel:self.channel.close()
        self.channel=None;self.metadata=None;self.refresh_at=0

    def authenticate(self):
        path=self.game_dir/'loginData.json'
        session=json.loads(path.read_text(encoding='utf-8-sig'))
        endpoint=validate_endpoint(session['star_network']['services_endpoint'])
        token=session['auth_token']
        if not isinstance(token,str) or token.count('.')!=2:raise ValueError('当前游戏登录信息格式不支持')
        if self.endpoint!=endpoint or self.channel is None:
            self.close();self.channel=grpc.secure_channel(endpoint,grpc.ssl_channel_credentials(),options=[('grpc.max_receive_message_length',16*1024*1024)])
            self.endpoint=endpoint
        raw=self.channel.unary_unary(METHODS['identity'])(b'',metadata=(('authorization','Bearer '+token),),timeout=12)
        player_token=text_field(fields(raw),2)
        if player_token.count('.')!=2:raise ValueError('未获取到当前角色会话')
        self.metadata=(('authorization','Bearer '+player_token),)
        try:
            claims=json.loads(base64.urlsafe_b64decode(player_token.split('.')[1]+'==='))
            self.refresh_at=min(time.time()+1800,float(claims['exp'])-90)
        except (ValueError,KeyError,TypeError):self.refresh_at=time.time()+600
        self.login_stamp=path.stat().st_mtime_ns

    def ensure_session(self):
        stamp=(self.game_dir/'loginData.json').stat().st_mtime_ns
        if not self.metadata or time.time()>=self.refresh_at or stamp!=self.login_stamp:self.authenticate()

    def call(self,method,request):
        if method not in ('groups','messages'):raise ValueError('unsupported read method')
        self.ensure_session()
        try:return self.channel.unary_unary(METHODS[method])(request,metadata=self.metadata,timeout=12)
        except grpc.RpcError as e:
            if e.code()==grpc.StatusCode.UNAUTHENTICATED:self.refresh_at=0
            raise

    def groups(self):
        # Group type 2 = public server group. No private chats, parties, or joins.
        response=fields(self.call('groups',blob(1,blob(1,varint(2)))))
        result=[]
        for item in response.get(1,[]):
            g=fields(item)
            if g.get(5,[0])[0]!=2:continue
            result.append({'id':text_field(g,1),'name':text_field(g,2)})
        return result

    def messages(self,group_id,first=30,after=''):
        if not 1<=first<=100:raise ValueError('invalid page size')
        paging=b'\x08'+varint(first)
        if after:paging+=blob(2,after.encode())
        response=fields(self.call('messages',blob(1,group_id.encode())+blob(2,paging)))
        result=[]
        for item in response.get(1,[]):
            m=fields(item)
            result.append({'id':text_field(m,1),'nickname':text_field(m,9) or text_field(m,4),
                           'original':text_field(m,5),'created_at':m.get(6,[0])[0],
                           'edited_at':m.get(8,[0])[0],'erased_at':m.get(7,[0])[0]})
        page=fields(response.get(2,[b''])[0])
        return result,{'has_next':bool(page.get(1,[0])[0]),'cursor':text_field(page,4)}

    def send_message(self,group_id,content):
        if not isinstance(content,str) or not content.strip() or len(content.encode('utf-8'))>4096:
            raise ValueError('消息为空或编码后过长，请缩短后重试')
        if group_id not in {g['id'] for g in self.groups()}:
            raise ValueError('当前账号已不在该公开频道，未发送')
        self.ensure_session()
        # This mutation is intentionally separate from the read/retry method.
        # Never retry automatically: a transport timeout can occur after server acceptance.
        request=blob(1,group_id.encode('utf-8'))+blob(2,content.encode('utf-8'))
        return self.channel.unary_unary(METHODS['send'])(request,metadata=self.metadata,timeout=12)
