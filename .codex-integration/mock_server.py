"""Deterministic JSON-RPC fixture. Never reads credentials or executes commands."""
import json, sys, pathlib, traceback
sys.excepthook = lambda t, e, tb: pathlib.Path(__file__).with_name('mock-error.txt').write_text(''.join(traceback.format_exception(t, e, tb)), encoding='utf-8')

scenario = sys.argv[1]
thread_id, turn_id, count = 'fixture-thread', '', 0
def send(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)
def event(method, params): send({'method':method, 'params':params})
def complete(status, text):
    event('item/completed', {'threadId':thread_id,'turnId':turn_id,'item':{'id':'commentary','type':'agentMessage','phase':'commentary','text':'COMMENTARY_MUST_NOT_REPLACE_RESULT'}})
    event('item/completed', {'threadId':thread_id,'turnId':turn_id,'item':{'id':'report','type':'agentMessage','phase':'final_answer','text':text}})
    event('turn/completed', {'threadId':thread_id,'turn':{'id':turn_id,'status':status,'error':None}})
for line in sys.stdin:
    msg=json.loads(line)
    with pathlib.Path(__file__).with_name('mock-trace.jsonl').open('a',encoding='utf-8') as trace: trace.write(json.dumps(msg,ensure_ascii=False)+'\n')
    method, params = msg.get('method'), msg.get('params',{})
    if method == 'initialize': send({'id':msg['id'],'result':{'userAgent':'mock'}})
    elif method == 'account/read':
        send({'id':msg['id'],'result':{'account':None if scenario=='unauth' else {'type':'chatgpt','planType':'plus'},'requiresOpenaiAuth':True}})
    elif method in ('thread/start','thread/resume'):
        assert params['sandbox']=='workspace-write' and params['approvalPolicy']=='on-request'
        assert params['approvalsReviewer']=='user'
        assert 'model' not in params, 'Do not force a default model'
        if method=='thread/resume': assert params['threadId']==thread_id
        send({'id':msg['id'],'result':{'thread':{'id':thread_id}}})
    elif method=='turn/start':
        count+=1; turn_id='turn-'+str(count)
        assert params['input'][0]['type']=='text'
        assert '用户本轮原始指令' in params['input'][0]['text']
        send({'id':msg['id'],'result':{'turn':{'id':turn_id,'status':'inProgress'}}})
        # Neither another task's success nor a previous turn's success may end this task.
        event('turn/completed',{'threadId':'wrong-thread','turn':{'id':turn_id,'status':'completed'}})
        event('turn/completed',{'threadId':thread_id,'turn':{'id':'old-turn','status':'completed'}})
        if scenario=='disconnect': sys.exit(0)
        elif scenario=='failure':
            event('turn/completed',{'threadId':thread_id,'turn':{'id':turn_id,'status':'failed','error':{'message':'fixture model error'}}})
        else:
            event('item/started',{'threadId':thread_id,'turnId':turn_id,'item':{'id':'command','type':'commandExecution','command':'FIXTURE ONLY - no execution','cwd':'fixture'}})
            send({'id':'approval-1','method':'item/commandExecution/requestApproval','params':{'threadId':thread_id,'turnId':turn_id,'itemId':'command','command':'FIXTURE ONLY - no execution'}})
    elif method=='turn/interrupt':
        send({'id':msg['id'],'result':{}}); complete('interrupted','任务已停止。')
    elif msg.get('id')=='approval-1':
        if scenario=='deny':
            assert msg['result']['decision']=='decline'; complete('interrupted','用户拒绝，未执行命令。')
        elif scenario=='cancel': continue
        else:
            assert msg['result']['decision']=='accept'
            event('item/started',{'threadId':thread_id,'turnId':turn_id,'item':{'id':'file','type':'fileChange','changes':[{'path':'fixture.txt','diff':'+ fixture'}]}})
            send({'id':22,'method':'item/fileChange/requestApproval','params':{'threadId':thread_id,'turnId':turn_id,'itemId':'file'}})
    elif msg.get('id')==22:
        assert msg['result']['decision']=='accept'
        send({'id':23,'method':'item/permissions/requestApproval','params':{'threadId':thread_id,'turnId':turn_id,'permissions':{'network':{'enabled':True}}}})
    elif msg.get('id')==23:
        assert msg['result']['permissions']['network']['enabled'] is True
        assert msg['result']['scope']=='turn'
        send({'id':24,'method':'item/tool/requestUserInput','params':{'threadId':thread_id,'turnId':turn_id,'questions':[{'id':'q','question':'Which output?','options':[{'label':'A','description':'fixture choice'}]}]}})
    elif msg.get('id')==24:
        assert msg['result']['answers']['q']['answers']==['A']
        send({'id':25,'method':'unsupported/request','params':{'threadId':thread_id,'turnId':turn_id}})
    elif msg.get('id')==25:
        assert 'error' in msg
        complete('completed','FIXTURE_VERIFIED 中文路径、输出与验证结果。')
