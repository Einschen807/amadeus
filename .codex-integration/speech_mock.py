import json,time,io,wave,math,struct,threading
from http.server import ThreadingHTTPServer,BaseHTTPRequestHandler
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def send(self,data,kind='application/json',code=200):
  self.send_response(code);self.send_header('Content-Type',kind);self.send_header('Content-Length',str(len(data)));self.end_headers()
  try:self.wfile.write(data)
  except (BrokenPipeError,ConnectionResetError):pass
 def do_GET(self):
  if self.path=='/openapi.json':self.send(json.dumps({'paths':{'/tts':{},'/set_gpt_weights':{}}}).encode())
  else:self.send(b'{}')
 def do_POST(self):
  d=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
  if self.path=='/tools':
   events=[{'tool_calls':[{'index':0,'id':'call_test','function':{'name':'open_url','arguments':'{"url":'}}]}, {'tool_calls':[{'index':0,'function':{'arguments':'"https://example.com"}'}}]}]
   out=''.join('data: '+json.dumps({'choices':[{'delta':e}]})+'\n\n' for e in events)+'data: [DONE]\n\n'
   self.send(out.encode(),'text/event-stream')
  elif self.path=='/chat':
   self.send_response(200);self.send_header('Content-Type','text/event-stream');self.end_headers()
   try:
    for text in ['{"ja":"最初の文です。"', ',"zh":"第一句。"}\n', '{"ja":"二番目の文です。","zh":"第二句！"}\n', '{"ja":"三番目の文です。","zh":"第三句？"}\n']:
     event=('data: '+json.dumps({'choices':[{'delta':{'content':text}}]},ensure_ascii=False)+'\n\n').encode()
     # Split a UTF-8 character across writes.
     at=event.find('第'.encode())+1
     self.wfile.write(event[:at]);self.wfile.flush();time.sleep(.03);self.wfile.write(event[at:]);self.wfile.flush();time.sleep(.7)
    time.sleep(2);self.wfile.write(b'data: [DONE]\n\n');self.wfile.flush()
   except (BrokenPipeError,ConnectionResetError):pass
  elif self.path=='/translate':
   text=d['messages'][-1]['content'];time.sleep(.1)
   self.send(json.dumps({'choices':[{'message':{'content':text}}]},ensure_ascii=False).encode())
  elif self.path=='/tts':
   if '超时' in d['text']:time.sleep(7)
   if '第二' in d['text'] or '二番目' in d['text']:self.send(b'{"message":"test sentence failure"}',code=500);return
   time.sleep(.2);out=io.BytesIO()
   with wave.open(out,'wb') as w:
    w.setnchannels(1);w.setsampwidth(2);w.setframerate(32000)
    w.writeframes(b''.join(struct.pack('<h',int(6000*math.sin(2*math.pi*220*i/32000))) for i in range(32000)))
   self.send(out.getvalue(),'audio/wav')
ThreadingHTTPServer(('127.0.0.1',18990),Handler).serve_forever()
