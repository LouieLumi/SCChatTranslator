"""Single-shot, explicit user sends. Disk requests are never replayed on restart."""
import json,re,time
from pathlib import Path
import grpc

class OutgoingSender:
    def __init__(self,home,write_json):
        self.home=Path(home);self.write_json=write_json
    def result(self,ident,ok,message,sent_text=''):
        self.write_json(self.home/'send-result.json',{'id':ident,'ok':ok,'message':message,'sent_text':sent_text})
    def cancel_stale(self):
        for name in ('send-request.json','send-processing.json'):
            path=self.home/name
            if path.exists():
                try:self.result(json.loads(path.read_text(encoding='utf-8-sig')).get('id',''),False,'上次请求已中断，未自动重发；请检查频道')
                except (OSError,ValueError):pass
                path.unlink(missing_ok=True)
    def process(self,client,translator,decoder,active,connected):
        request=self.home/'send-request.json';claimed=self.home/'send-processing.json'
        if not request.exists():return
        ident='';sending=False
        try:
            request.replace(claimed)
            data=json.loads(claimed.read_text(encoding='utf-8-sig'));ident=data['id']
            if not re.fullmatch(r'[a-f0-9]{32}',ident):raise ValueError('发送请求无效')
            if not 0<=time.time()-float(data['created_at'])<=90:raise ValueError('发送请求已过期，请重新发送')
            if not connected or not client or not active or data['group_id']!=active:raise ValueError('服务器频道已变化，请核对频道后重试')
            value=data['text'].strip()
            if not value or len(value)>500:raise ValueError('请输入 1–500 字的消息')
            if data['language']=='zh':wire=decoder.encode(value);display=value
            elif data['language']=='en':
                display=translator.to_english(value).strip();wire=display
                if not wire or re.search(r'[\u3400-\u9fff\ufffd]',wire):raise ValueError('未获得完整英文译文，原文已保留')
            else:raise ValueError('发送语言无效')
            if time.time()-float(data['created_at'])>90:raise ValueError('翻译耗时过长，请重新发送')
            # Re-check the explicit channel selection after translation.
            try:selected=json.loads((self.home/'selection.json').read_text(encoding='utf-8-sig')).get('id','')
            except (OSError,ValueError):selected=''
            if selected and selected!=active:raise ValueError('所选频道已改变，未发送')
            sending=True;client.send_message(active,wire)
            self.result(ident,True,'已发送'+(' · '+display if data['language']=='en' else ' · 中文编码'),display)
        except grpc.RpcError as e:
            if e.code() in (grpc.StatusCode.UNAVAILABLE,grpc.StatusCode.DEADLINE_EXCEEDED,grpc.StatusCode.CANCELLED,grpc.StatusCode.UNKNOWN):
                message='未确认送达，请先检查频道，避免重复发送'
            elif e.code()==grpc.StatusCode.UNAUTHENTICATED:message='游戏会话已过期，未发送'
            elif e.code()==grpc.StatusCode.PERMISSION_DENIED:message='游戏服务拒绝发送：当前会话无发送权限'
            else:message='游戏服务拒绝发送（'+e.code().name+'），原文已保留'
            self.result(ident,False,message)
        except (ValueError,KeyError,TypeError,OSError) as e:
            self.result(ident,False,str(e)[:150] if not sending else '发送失败，请核对频道后重试')
        finally:claimed.unlink(missing_ok=True)
