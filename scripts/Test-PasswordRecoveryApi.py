#!/usr/bin/env python3
"""Real HTTP/MySQL/SMTP recovery integration for all three independent applications.
Uses an isolated Docker MySQL database, local SMTP capture and test-only Provider adapter.
Never connects to production services, real mailboxes or user databases.
Requires a prior dotnet build Zumaris.sln; docker/mysql:8.0 and .NET 8.
"""
import concurrent.futures, datetime, email, email.policy, hashlib, http.server, json, os, pathlib, re, secrets, socketserver, subprocess, tempfile, threading, time, urllib.error, urllib.parse, urllib.request, uuid
ROOT=pathlib.Path(__file__).resolve().parents[1]
CONTAINER='zumaris-recovery-'+uuid.uuid4().hex[:10]
PORTS={'Identity':15205,'Notification':15213,'Provider':15211,'Smtp':15225}
KEY=secrets.token_urlsafe(48);EMAIL='person@example.test';OLD='Previous-password-123';NEW='New-password-456'
IDS={app:str(uuid.uuid4()) for app in ['App','Tech','Admin']};PROVIDER=str(uuid.uuid4());messages=[];lock=threading.Lock();processes={};logs={};checks=0
class Provider(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  body=json.dumps({'payload':{'userId':IDS['Tech'],'providerId':PROVIDER,'isActive':True,'hasSignedUp':True}}).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(body)
 def log_message(self,*args):pass
class Smtp(socketserver.StreamRequestHandler):
 def handle(self):
  self.wfile.write(b'220 local-recovery-test\r\n');data=[]
  while True:
   line=self.rfile.readline()
   if not line:return
   verb=line.upper().split(b' ',1)[0].strip()
   if verb in [b'EHLO',b'HELO']:self.wfile.write(b'250-local-recovery-test\r\n250 8BITMIME\r\n')
   elif verb in [b'MAIL',b'RCPT',b'RSET',b'NOOP']:self.wfile.write(b'250 OK\r\n')
   elif verb==b'DATA':
    self.wfile.write(b'354 End with dot\r\n');data=[]
    while True:
     row=self.rfile.readline()
     if not row or row==b'.\r\n':break
     data.append(row[1:] if row.startswith(b'..') else row)
    with lock:messages.append(email.message_from_bytes(b''.join(data),policy=email.policy.default))
    self.wfile.write(b'250 Accepted\r\n')
   elif verb==b'QUIT':self.wfile.write(b'221 Bye\r\n');return
   else:self.wfile.write(b'502 Unsupported\r\n')
class SmtpServer(socketserver.ThreadingTCPServer):allow_reuse_address=True;daemon_threads=True

def sql(text):
 r=subprocess.run(['docker','exec','-i',CONTAINER,'mysql','-uroot','--batch','--skip-column-names'],input=text,text=True,capture_output=True)
 if r.returncode:raise RuntimeError(r.stderr)
 return r.stdout.strip()
def call(path,body=None,expected=200,method='POST',token=None,cookie=None,csrf=True,service='Identity'):
 global checks
 headers={'Content-Type':'application/json'}
 if csrf:headers['X-Zumaris-CSRF']='1'
 if token:headers['Authorization']='Bearer '+token
 if cookie:headers['Cookie']=cookie
 request=urllib.request.Request(f'http://127.0.0.1:{PORTS[service]}/'+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method)
 try:
  with urllib.request.urlopen(request,timeout=30) as r:status=r.status;data=r.read();response_headers=r.headers
 except urllib.error.HTTPError as e:status=e.code;data=e.read();response_headers=e.headers
 value=json.loads(data) if data else None
 assert status==expected,(path,status,expected,value.get('message') if isinstance(value,dict) else None)
 checks+=1;return value,response_headers

def start(service,env,folder):
 p=ROOT/f'src/Services/{service}/{service}.Api';child=env.copy();child['ASPNETCORE_URLS']=f'http://127.0.0.1:{PORTS[service]}'
 log=open(pathlib.Path(folder)/(service+'.log'),'a');logs[service]=log
 processes[service]=subprocess.Popen([os.environ.get('DOTNET_ROOT','/workspace/.dotnet')+'/dotnet',str(p/f'bin/Debug/net8.0/{service}.Api.dll')],cwd=p,env=child,stdout=log,stderr=subprocess.STDOUT)
 for _ in range(100):
  if processes[service].poll() is not None:raise RuntimeError(service+' failed to start; inspect local test log')
  try:urllib.request.urlopen(f'http://127.0.0.1:{PORTS[service]}/swagger/v1/swagger.json',timeout=1);return
  except OSError:time.sleep(.1)
 raise RuntimeError(service+' failed readiness')
def stop(service):
 p=processes.pop(service);p.terminate()
 try:p.wait(timeout=10)
 except subprocess.TimeoutExpired:p.kill();p.wait()
 logs.pop(service).close()
def wait_for(test,seconds=30):
 until=time.time()+seconds
 while time.time()<until:
  if test():return
  time.sleep(.2)
 raise AssertionError('Timed out waiting for local recovery delivery')
def mail_tokens():
 result={}
 with lock:
  for message in messages:
   content=message.get_content()
   m=re.search(r'https://(app|tech|admin)\.zumaris\.com/reset-password#token=([A-Za-z0-9_-]{43})',content)
   if m:result[m[1]]=m[2]
 return result

if __name__=='__main__':
 provider=http.server.ThreadingHTTPServer(('127.0.0.1',PORTS['Provider']),Provider);threading.Thread(target=provider.serve_forever,daemon=True).start();smtp=None
 try:
  subprocess.run(['docker','run','-d','--name',CONTAINER,'-p','127.0.0.1:33307:3306','-e','MYSQL_ALLOW_EMPTY_PASSWORD=yes','mysql:8.0','--log-bin-trust-function-creators=1'],check=True,capture_output=True)
  for _ in range(90):
   r=subprocess.run(['docker','exec',CONTAINER,'mysqladmin','-h127.0.0.1','ping','--silent'],capture_output=True)
   if r.returncode==0:
    try:sql('SELECT 1;');break
    except RuntimeError:pass
   time.sleep(1)
  # Destructive account fixture runs ONLY inside this newly created disposable Docker database.
  for name in ['008_reset_independent_accounts.sql','009_email_username_capacity.sql','010_password_recovery.sql']:sql((ROOT/'database/identity'/name).read_text())
  sql((ROOT/'database/identity/010_password_recovery.sql').read_text())
  salt=secrets.token_bytes(16);hashed=hashlib.pbkdf2_hmac('sha256',OLD.encode(),salt,10000).hex().upper()+':'+salt.hex().upper()+':10000:SHA256'
  for app,id in IDS.items():sql(f"USE zumaris_identity; INSERT INTO {app.lower()}_accounts(id,user_name,email,password_hash,created_at)VALUES('{id}','{EMAIL}','{EMAIL}','{hashed}',UTC_TIMESTAMP(6));")
  sql("CREATE USER 'recovery_test'@'%' IDENTIFIED BY ''; GRANT SELECT,INSERT,UPDATE ON zumaris_identity.* TO 'recovery_test'@'%'; CREATE DATABASE zumaris_independent_identity_test; GRANT ALL ON zumaris_independent_identity_test.* TO 'recovery_test'@'%';")
  with tempfile.TemporaryDirectory(prefix='zumaris-recovery-') as folder:
   env=os.environ.copy();env.update({'ASPNETCORE_ENVIRONMENT':'Development','Jwt__Key':KEY,'Jwt__Issuer':'recovery-tests','Jwt__Audience':'recovery-clients','PasswordRecovery__Enabled':'true','DataProtection__KeysPath':folder+'/keys','Notification__BaseUrl':f'http://127.0.0.1:{PORTS["Notification"]}/','Provider__BaseUrl':f'http://127.0.0.1:{PORTS["Provider"]}/','Smtp__Host':'127.0.0.1','Smtp__Port':str(PORTS['Smtp']),'Smtp__EnableTls':'false','Smtp__FromAddress':'noreply@example.test','ConnectionStrings__DefaultConnection':'Server=127.0.0.1;Port=33307;Database=zumaris_identity;User ID=recovery_test;AllowPublicKeyRetrieval=True;SSL Mode=None'})
   start('Notification',env,folder);start('Identity',env,folder)
   sessions={}
   for side in ['app','tech','admin']:
    value,headers=call(f'api/{side}/login',{'email':EMAIL,'password':OLD});sessions[side]=(value['payload']['accessToken'],headers.get('Set-Cookie').split(';',1)[0])
   requested,_=call('api/app/forgot-password',{'email':EMAIL},202)
   unknown,_=call('api/app/forgot-password',{'email':'unknown@example.test'},202);assert unknown==requested
   call('api/app/forgot-password',{'email':EMAIL},202)
   for side in ['tech','admin']:call(f'api/{side}/forgot-password',{'email':EMAIL},202)
   assert sql('SELECT COUNT(*) FROM zumaris_identity.identity_password_resets;')=='3'
   call('api/app/forgot-password',{'email':EMAIL},429)
   # SMTP is deliberately offline: recovery requests remain queued and generic 202 still holds.
   wait_for(lambda:int(sql('SELECT COALESCE(MAX(attempts),0) FROM zumaris_identity.identity_password_resets;'))>=1)
   assert sql("SELECT COUNT(*) FROM zumaris_identity.identity_password_resets WHERE delivery_status='Queued' AND protected_payload IS NOT NULL;")=='3'
   stop('Identity')
   smtp=SmtpServer(('127.0.0.1',PORTS['Smtp']),Smtp);threading.Thread(target=smtp.serve_forever,daemon=True).start()
   sql("UPDATE zumaris_identity.identity_password_resets SET next_attempt_at=UTC_TIMESTAMP(6),lease_id=NULL,lease_until=NULL WHERE delivery_status='Queued';")
   start('Identity',env,folder)
   wait_for(lambda:len(mail_tokens())==3)
   wait_for(lambda:sql("SELECT COUNT(*) FROM zumaris_identity.identity_password_resets WHERE delivery_status='Sent' AND protected_payload IS NULL;")=='3')
   tokens=mail_tokens()
   for raw in tokens.values():assert raw not in sql('SELECT token_hash FROM zumaris_identity.identity_password_resets;')
   # End-user JWTs cannot call the internal mail sender.
   call('api/internal/password-recovery-email',{},401,token=sessions['app'][0],service='Notification')
   call('api/app/forgot-password',{'email':EMAIL},400,csrf=False)
   call('api/tech/reset-password',{'token':tokens['app'],'newPassword':NEW,'confirmPassword':NEW},400)
   call('api/app/reset-password',{'token':tokens['app'],'newPassword':NEW,'confirmPassword':'mismatch'},400)
   # Concurrent replay must commit the password change once.
   def reset_once():
    try:call('api/app/reset-password',{'token':tokens['app'],'newPassword':NEW,'confirmPassword':NEW});return True
    except AssertionError as e:
     if e.args and e.args[0][1]==400:return False
     raise
   with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:assert sum(pool.map(lambda _:reset_once(),range(2)))==1
   call('api/auth/session',None,401,method='GET',token=sessions['app'][0])
   for side in ['tech','admin']:call('api/auth/session',None,204,method='GET',token=sessions[side][0])
   call('api/app/login',{'email':EMAIL,'password':OLD},401)
   call('api/app/login',{'email':EMAIL,'password':NEW})
   call('api/app/refresh',{},401,cookie=sessions['app'][1])
   for side in ['tech','admin']:
    call(f'api/{side}/reset-password',{'token':tokens[side],'newPassword':NEW,'confirmPassword':NEW})
    call('api/auth/session',None,401,method='GET',token=sessions[side][0])
    call(f'api/{side}/login',{'email':EMAIL,'password':OLD},401)
    call(f'api/{side}/login',{'email':EMAIL,'password':NEW})
   call('api/admin/reset-password',{'token':tokens['admin'],'newPassword':NEW,'confirmPassword':NEW},400)
   assert sql("SELECT COUNT(*) FROM zumaris_identity.zumaris_auth_sessions WHERE revocation_reason='password_reset';")=='3'
   # Expired links are not accepted, even when the signature/format is otherwise valid.
   stop('Identity');start('Identity',env,folder)
   sql("UPDATE zumaris_identity.identity_password_resets SET created_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 2 MINUTE) WHERE application='App';")
   before=len(messages);call('api/app/forgot-password',{'email':EMAIL},202)
   wait_for(lambda:len(messages)>before)
   latest=mail_tokens()['app'];assert latest!=tokens['app']
   sql("UPDATE zumaris_identity.identity_password_resets SET expires_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 SECOND),created_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 20 MINUTE) WHERE application='App' AND used_at IS NULL;")
   call('api/app/reset-password',{'token':latest,'newPassword':OLD,'confirmPassword':OLD},400)
   # Run the existing guarded SQL suite plus the stale-login credential proof on a separate schema.
   testenv=os.environ.copy();testenv['ZUMARIS_INDEPENDENT_IDENTITY_TEST_CONNECTION']='Server=127.0.0.1;Port=33307;Database=zumaris_independent_identity_test;User ID=recovery_test;AllowPublicKeyRetrieval=True;SSL Mode=None'
   logpath=pathlib.Path(folder)/'sql-tests.log'
   with open(logpath,'w') as output:r=subprocess.run([os.environ.get('DOTNET_ROOT','/workspace/.dotnet')+'/dotnet','test','tests/Identity.Tests/Identity.Tests.csproj','--no-build','--no-restore','--filter','FullyQualifiedName~IndependentAccountsMySqlTests'],cwd=ROOT,env=testenv,stdout=output,stderr=subprocess.STDOUT)
   if r.returncode:raise AssertionError('SQL regression failed: '+logpath.read_text()[-3000:])
   for service in list(processes):stop(service)
  print(f'PASS: {checks} HTTP checks; all App/Tech/Admin flows, actual local SMTP delivery, durable encrypted retries/restart, token replay/expiry, scoped session revocation and SQL credential-race regression.')
 finally:
  for service in list(processes):stop(service)
  if smtp:smtp.shutdown();smtp.server_close()
  provider.shutdown();subprocess.run(['docker','rm','-f',CONTAINER],capture_output=True)
