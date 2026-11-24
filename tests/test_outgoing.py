import json,sys,tempfile,time,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'src'))
from chat_decoder import ChatDecoder
from outgoing import OutgoingSender
from client import ChatClient,fields,text_field

class FakeClient:
    def __init__(self):self.sent=[]
    def send_message(self,group,text):self.sent.append((group,text))
class Translate:
    def to_english(self,value):return 'Hello, pilots!'
class SendTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
        self.decoder=ChatDecoder(self.root);self.decoder.mapping={'IH':'你','E8':'好','1P9':'。'}
        self.client=FakeClient();self.sender=OutgoingSender(self.root,lambda p,d:p.write_text(json.dumps(d)))
    def tearDown(self):self.tmp.cleanup()
    def request(self,**kwargs):
        data={'id':'a'*32,'group_id':'public-test','text':'你好','language':'zh','created_at':time.time()};data.update(kwargs)
        (self.root/'send-request.json').write_text(json.dumps(data));return data
    def result(self):return json.loads((self.root/'send-result.json').read_text())
    def process(self,active='public-test',connected=True):self.sender.process(self.client,Translate(),self.decoder,active,connected)
    def test_chinese_encoder_roundtrip(self):
        text='你好。';wire=self.decoder.encode(text);self.assertEqual(wire,'[zh] @IH@E8@1P9');self.assertEqual(self.decoder.decode(wire),(text,True))
    def test_encoder_word_boundary(self):self.assertEqual(self.decoder.encode('你好ABC'),'[zh] @IH@E8 ABC')
    def test_unknown_glyph_never_sent(self):
        self.request(text='你好龘');self.process();self.assertFalse(self.result()['ok']);self.assertEqual(self.client.sent,[])
    def test_chinese_sends_encoded_once(self):
        self.request();self.process();self.process();self.assertEqual(self.client.sent,[('public-test','[zh] @IH@E8')])
    def test_english_translates_before_send(self):
        self.request(language='en');self.process();self.assertEqual(self.client.sent,[('public-test','Hello, pilots!')])
    def test_changed_channel_blocks(self):
        self.request();self.process(active='other');self.assertFalse(self.result()['ok']);self.assertEqual(self.client.sent,[])
    def test_selected_channel_changed_blocks(self):
        self.request();(self.root/'selection.json').write_text('{"id":"other"}');self.process();self.assertEqual(self.client.sent,[])
    def test_expired_request_blocks(self):
        self.request(created_at=time.time()-100);self.process();self.assertEqual(self.client.sent,[])
    def test_restart_cancels_without_replay(self):
        self.request();self.sender.cancel_stale();self.process();self.assertEqual(self.client.sent,[]);self.assertFalse(self.result()['ok'])
    def test_send_wire_and_membership(self):
        client=ChatClient(self.root);sent=[]
        client.groups=lambda:[{'id':'public-test'}];client.ensure_session=lambda:None;client.metadata=()
        class Channel:
            def unary_unary(self,path):
                def call(request,**kw):sent.append((path,fields(request)));return b''
                return call
        client.channel=Channel();client.send_message('public-test','Hello')
        self.assertEqual(text_field(sent[0][1],1),'public-test');self.assertEqual(text_field(sent[0][1],2),'Hello')
        with self.assertRaises(ValueError):client.send_message('private-other','Hello')
        self.assertEqual(len(sent),1)
if __name__=='__main__':unittest.main()
