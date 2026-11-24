import argparse
import datetime
import json
import os
import re
import threading
import time
from pathlib import Path
import grpc
from client import ChatClient
from chat_decoder import ChatDecoder
from translator_api import TranslationRouter
from game_glossary import GameGlossary
from outgoing import OutgoingSender

ROOT=Path(__file__).resolve().parent
HOME=Path(os.environ.get('SCCHAT_HOME',ROOT))

def atomic_json(path,data):
    temp=path.with_suffix('.tmp')
    temp.write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
    for i in range(5):
        try:os.replace(temp,path);return
        except PermissionError:
            if i==4:raise
            time.sleep(.04)

class LocalTranslator:
    def __init__(self,direction='en_zh'):
        self.direction=direction
        self.glossary=GameGlossary()
        import ctranslate2
        import sentencepiece
        matches=list((ROOT/'models'/('translate-'+direction+'-1_9')).glob('sentencepiece.model'))
        if not matches:raise RuntimeError('本地翻译模型缺失')
        self.spm=sentencepiece.SentencePieceProcessor(model_file=str(matches[0]))
        self.engine=ctranslate2.Translator(str(matches[0].parent/'model'),device='cpu',compute_type='int8',inter_threads=1,intra_threads=2)

    def translate(self,value):
        if self.direction=='zh_en':return self.infer(value)
        if re.match(r'^\s*\[zh\]',value,re.I) and re.search(r'@[A-Za-z0-9]{2,}',value):
            return '中文字体编码消息：保留原文，尚未解码'
        if re.search(r'[\u4e00-\u9fff]',value):return '中文消息'
        if not re.search(r'[A-Za-z]{2,}',value):return ''
        return self.glossary.translate_with_terms(value,self.infer)

    def infer(self,value):
        if self.direction!='zh_en' and not re.search(r'[A-Za-z]{2,}',value):return value
        # Bound token count and CPU work; never execute content received from chat.
        tokens=self.spm.encode(value[:2500],out_type=str)
        result=self.engine.translate_batch([tokens],beam_size=2,max_input_length=384,max_decoding_length=384)
        return self.spm.decode_pieces(result[0].hypotheses[0]).replace('▁',' ').strip()

def run(once=False):
    data_dir=HOME/'data';data_dir.mkdir(exist_ok=True)
    client=None;decoder=None;translator=None;settings_stamp=None;settings_path=HOME/'settings.json'
    sender=OutgoingSender(data_dir,atomic_json)
    sender.cancel_stale()
    state={'status':'正在准备翻译引擎…','groups':[],'group':'','group_id':'','updated_at':'','messages':[],'version':0}
    def publish(status=None):
        if translator:state['translation_backend']=translator.label
        if status:state['status']=status
        state['version']+=1
        atomic_json(data_dir/'state.json',state)
    publish()
    records={};groups_at=0;active=None;delay=3
    try:
        while not (data_dir/'stop').exists():
            if (data_dir/'pause').exists():
                if state['status']!='已暂停读取':publish('已暂停读取')
                time.sleep(.5);continue
            try:
                stamp=settings_path.stat().st_mtime_ns
                if stamp!=settings_stamp:
                    config=json.loads(settings_path.read_text(encoding='utf-8-sig'))
                    if translator:translator.close()
                    if client:client.close()
                    translator=TranslationRouter(config,LocalTranslator,HOME,lambda:LocalTranslator('zh_en'))
                    game=config.get('game_dir','')
                    client=ChatClient(game) if game else None
                    decoder=ChatDecoder(game) if game else None
                    settings_stamp=stamp;records.clear();groups_at=0;active=None
                    state['messages']=[];state['groups']=[]
                sender.process(client,translator,decoder,state.get('group_id',''),state['status'].startswith('已连接 · '))
                if client is None:
                    publish('等待游戏：请在设置中选择 LIVE 目录');time.sleep(2);continue
                if time.monotonic()-groups_at>30 or not state['groups']:
                    decoder.refresh()
                    state['groups']=client.groups();groups_at=time.monotonic()
                if not state['groups']:
                    publish('等待加入游戏服务器…');time.sleep(3);continue
                selection=''
                try:selection=json.loads((data_dir/'selection.json').read_text(encoding='utf-8-sig'))['id']
                except (OSError,ValueError,KeyError):pass
                candidates={g['id']:g for g in state['groups']}
                if selection in candidates:group=candidates[selection]
                elif len(candidates)==1:group=next(iter(candidates.values()))
                else:
                    publish('检测到多个服务器频道，请在设置中选择');time.sleep(3);continue
                if active!=group['id']:
                    records.clear();active=group['id'];state['messages']=[]
                    state['group_id']=active;state['group']=group['name']
                messages,page=client.messages(active,first=30)
                # Catch short bursts missed between polls, without unbounded history requests.
                pages=1
                while records and messages and not any(m['id'] in records for m in messages) and page['has_next'] and page['cursor'] and pages<4:
                    more,page=client.messages(active,first=30,after=page['cursor']);messages+=more;pages+=1
                    if not more:break
                new=[]
                for m in sorted(messages,key=lambda x:x['created_at']):
                    if m.get('erased_at'):
                        records.pop(m['id'],None);continue
                    old=records.get(m['id'])
                    if old and old['original']==m['original']:continue
                    m['time']=datetime.datetime.fromtimestamp(m['created_at']/1000).strftime('%H:%M:%S')
                    m['display_original'],m['is_chinese']=decoder.decode(m['original'])
                    m['term_reference']='\n'.join(translator.glossary.references(m['original'])) if not m['is_chinese'] else ''
                    m['translation']='' if m['is_chinese'] else '正在翻译…'
                    records[m['id']]=m
                    if not m['is_chinese']:new.append(m)
                records=dict(sorted(records.items(),key=lambda x:x[1]['created_at'])[-150:])
                state['messages']=list(records.values())
                state['updated_at']=datetime.datetime.now().strftime('%H:%M:%S')
                state['translation_backend']=translator.label
                publish('已连接 · '+translator.label+(' · 正在翻译新消息' if new else ''))
                for m in new:
                    sender.process(client,translator,decoder,active,True)
                    if (data_dir/'stop').exists() or settings_path.stat().st_mtime_ns!=settings_stamp:break
                    try:
                        m['translation']=translator.translate(m['original'])
                        m['translation_engine']=translator.label
                    except Exception:m['translation']='本条翻译失败，已保留原文'
                    state['translation_backend']=translator.label
                    publish()
                publish('已连接 · '+translator.label)
                delay=3
                if once:return 0
            except FileNotFoundError:
                publish('未找到游戏登录信息，请先打开启动器并进入游戏')
                delay=min(delay*2,30)
            except grpc.RpcError as e:
                labels={grpc.StatusCode.UNAUTHENTICATED:'游戏会话已过期，请重新登录启动器',grpc.StatusCode.PERMISSION_DENIED:'当前会话无权读取该频道'}
                publish(labels.get(e.code(),'聊天服务暂时不可达，正在重试'))
                delay=min(delay*2,30)
            except (ValueError,KeyError,OSError) as e:
                publish('读取失败：'+str(e)[:140]);delay=min(delay*2,30)
            if once:return 1
            for _ in range(int(delay*5)):
                sender.process(client,translator,decoder,state.get('group_id',''),state['status'].startswith('已连接 · '))
                if (data_dir/'stop').exists() or (settings_path.exists() and settings_path.stat().st_mtime_ns!=settings_stamp):break
                time.sleep(.2)
    finally:
        if client:client.close()
        if translator:translator.close()
    return 0

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--once',action='store_true')
    raise SystemExit(run(parser.parse_args().once))
