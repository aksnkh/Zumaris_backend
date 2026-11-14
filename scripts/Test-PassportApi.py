#!/usr/bin/env python3
"""Integration test of real Passport services on disposable loopback MySQL.
Requires docker, .NET 8 and a prior dotnet build Zumaris.sln. Never uses user DB configuration.
The Identity stub tests session rejection/unavailability; it is NOT production authentication.
"""
import base64, datetime, hashlib, hmac, http.server, json, os, pathlib, secrets, subprocess, tempfile, threading, time, urllib.request, urllib.error, uuid
ROOT=pathlib.Path(__file__).resolve().parents[1]
container='zumaris-passport-'+uuid.uuid4().hex[:10]
processes={}; logs={}; tests=0; revoked=set(); identity_status=204
KEY=secrets.token_urlsafe(48)
ports={'Property':15107,'Document':15108,'Maintenance':15109,'ServiceRequest':15106,'Identity':15105,'Gateway':15039}
user=str(uuid.uuid4()); other=str(uuid.uuid4()); reader=str(uuid.uuid4())
def token(who=user,app='App'):
 def enc(x):return base64.urlsafe_b64encode(json.dumps(x,separators=(',',':')).encode()).rstrip(b'=').decode()
 p={'sub':who,'sid':str(uuid.uuid4()),'application':app,'role':{'App':'Customer','Tech':'Provider','Admin':'Admin'}[app],'iss':'passport-tests','aud':'passport-tests','exp':int(time.time())+3600}
 if app=='Tech':p['providerId']=str(uuid.uuid4())
 s=enc({'alg':'HS256','typ':'JWT'})+'.'+enc(p)
 return s+'.'+base64.urlsafe_b64encode(hmac.new(KEY.encode(),s.encode(),hashlib.sha256).digest()).rstrip(b'=').decode()
owner_token=token();other_token=token(other);reader_token=token(reader)
class Identity(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  t=self.headers.get('Authorization','')[7:]
  self.send_response(401 if t in revoked else identity_status);self.end_headers()
 def log_message(self,*a):pass
def sql(value,check=True):
 r=subprocess.run(['docker','exec','-i',container,'mysql','-uroot','--batch','--skip-column-names'],input=value,text=True,capture_output=True)
 if check and r.returncode:raise RuntimeError(r.stderr)
 return r.stdout.strip()
def call(service,path,method='GET',data=None,t=owner_token,expected=200,raw=False,headers=None):
 global tests
 h={} if t is None else {'Authorization':'Bearer '+t}
 if headers:h.update(headers)
 if data is not None and not isinstance(data,bytes):data=json.dumps(data).encode();h['Content-Type']='application/json'
 req=urllib.request.Request(f'http://127.0.0.1:{ports[service]}/'+path,method=method,data=data,headers=h)
 try:
  with urllib.request.urlopen(req,timeout=35) as r:code=r.status;body=r.read()
 except urllib.error.HTTPError as e:code=e.code;body=e.read()
 if code!=expected:
  debug=(pathlib.Path(folder)/(service+'.log')).read_text()[-6500:]
  raise AssertionError((service,path,code,body[:900],debug))
 tests+=1
 if raw:return body
 return json.loads(body) if body else None
def payload(*a,**k):return call(*a,**k)['payload']
def start(service,baseenv,folder):
 env=baseenv.copy();env['ASPNETCORE_URLS']=f'http://127.0.0.1:{ports[service]}'
 if service=='Gateway':
  project=ROOT/'src/Gateways/YarpGateway';assembly='YarpGateway'
  for name in ['Property','Maintenance','Document','ServiceRequest']:env[f'ReverseProxy__Clusters__{name.lower() if name!="ServiceRequest" else "service-request"}-cluster__Destinations__{name.lower() if name!="ServiceRequest" else "service-request"}-srv__Address']=f'http://127.0.0.1:{ports[name]}'
 else:
  route={'ServiceRequest':'service_request'}.get(service,service.lower());env['ConnectionStrings__DefaultConnection']=f'Server=127.0.0.1;Port=33306;Database=zumaris_{route};User ID=passport_test;AllowPublicKeyRetrieval=True;SSL Mode=None'
  project=ROOT/f'src/Services/{service}/{service}.Api';assembly=f'{service}.Api'
 log=open(pathlib.Path(folder)/(service+'.log'),'w');logs[service]=log
 dotnet=os.environ.get('DOTNET_ROOT','/workspace/.dotnet')+'/dotnet'
 processes[service]=subprocess.Popen([dotnet,str(project/f'bin/Debug/net8.0/{assembly}.dll')],cwd=project,env=env,stdout=log,stderr=subprocess.STDOUT)
 for _ in range(100):
  if processes[service].poll() is not None:raise RuntimeError(f'{service} exited: '+(pathlib.Path(folder)/(service+'.log')).read_text()[-3000:])
  try:
   urllib.request.urlopen(f'http://127.0.0.1:{ports[service]}/swagger/v1/swagger.json',timeout=1);return
  except (OSError,urllib.error.URLError):time.sleep(.1)
 raise RuntimeError(service+' did not start')
def stop(name):
 p=processes.pop(name);p.terminate()
 try:p.wait(timeout=10)
 except subprocess.TimeoutExpired:p.kill();p.wait()
 logs.pop(name).close()
def upload(p,body,filename='certificate.pdf',related=None,t=owner_token,expected=201):
 boundary='passport'+uuid.uuid4().hex
 values={'title':'Gas certificate','type':'Certificate'}
 if related:values.update(related)
 data=b''
 for key,value in values.items():data+=f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode()
 data+=f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{filename}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()+body+f'\r\n--{boundary}--\r\n'.encode()
 return call('Document',f'api/properties/{p}/documents','POST',data,t,expected,headers={'Content-Type':'multipart/form-data; boundary='+boundary})
if __name__=='__main__':
 server=http.server.ThreadingHTTPServer(('127.0.0.1',ports['Identity']),Identity);threading.Thread(target=server.serve_forever,daemon=True).start()
 try:
  subprocess.run(['docker','run','-d','--name',container,'-p','127.0.0.1:33306:3306','-e','MYSQL_ALLOW_EMPTY_PASSWORD=yes','mysql:8.0'],check=True,capture_output=True)
  for _ in range(90):
   r=subprocess.run(['docker','exec',container,'mysqladmin','ping','--silent'],capture_output=True)
   if r.returncode==0:
    try:
     sql('SELECT 1;')
     # Wait for the final TCP listener, not the entrypoint temporary socket-only server.
     subprocess.run(['docker','exec',container,'mysqladmin','-h127.0.0.1','ping','--silent'],check=True,capture_output=True);break
    except (RuntimeError,subprocess.CalledProcessError):pass
   time.sleep(1)
  migrations=['property/001_properties.sql','property/002_passport.sql','maintenance/001_property_equipment.sql','document/001_property_documents.sql','service-request/001_service_requests.sql','service-request/002_request_lifecycle.sql','service-request/003_service_history.sql','service-request/008_property_activity_index.sql']
  for name in migrations:sql((ROOT/'database'/name).read_text())
  # New scripts are additive and repeatable.
  for name in ['property/002_passport.sql','maintenance/001_property_equipment.sql','document/001_property_documents.sql','service-request/008_property_activity_index.sql']:sql((ROOT/'database'/name).read_text())
  sql("CREATE USER 'passport_test'@'%' IDENTIFIED BY '';"+''.join(f'GRANT SELECT,INSERT,UPDATE ON zumaris_{x}.* TO \'passport_test\'@\'%\';' for x in ['property','maintenance','document','service_request']))
  with tempfile.TemporaryDirectory(prefix='passport-api-') as folder:
   env=os.environ.copy();env.update({'ASPNETCORE_ENVIRONMENT':'Development','Jwt__Key':KEY,'Jwt__Issuer':'passport-tests','Jwt__Audience':'passport-tests','Documents__RootPath':folder+'/files','Storage__RootPath':folder+'/request-photos','DOTNET_CLI_TELEMETRY_OPTOUT':'1'})
   for n in ['Identity','Property','Maintenance','Document','ServiceRequest']:env[n+'__BaseUrl']=f'http://127.0.0.1:{ports[n]}/'
   for n in ['Property','Maintenance','Document','ServiceRequest','Gateway']:start(n,env,folder)
   property_body={'addressLine':'12 Example Road','postcode':'SW1A 1AA','city':'London','countryCode':'GB','propertyType':'Detached','bedrooms':2,'bathrooms':1,'ownershipRole':'Owner'}
   p=call('Property','api/properties','POST',property_body,expected=201)['propertyId']
   p2=call('Property','api/properties','POST',property_body,t=other_token,expected=201)['propertyId']
   base=f'api/properties/{p}'
   sql(f"USE zumaris_property; INSERT INTO property_memberships(property_id,user_id,ownership_role,is_active,created_at)VALUES('{p}','{reader}','Tenant',1,UTC_TIMESTAMP(6));")
   for svc,suffix in [('Maintenance','equipment'),('Document','documents'),('Property','rooms'),('Property','events'),('Property','timeline'),('Property','passport/metrics')]:
    call(svc,base+'/'+suffix,t=other_token,expected=404)
    call(svc,base+'/'+suffix,t=None,expected=401)
    call(svc,base+'/'+suffix,t=token(user,'Tech'),expected=403)
   call('Maintenance',base+'/equipment',t=reader_token)
   equipment={'name':'Boiler','category':'Heating','status':'Active','brand':'Test brand','installedOn':'2020-01-01','warrantyExpiresOn':'2030-01-01','nextServiceOn':'2027-01-01'}
   eq=payload('Maintenance',base+'/equipment','POST',equipment,expected=201)['equipmentId']
   call('Maintenance',base+'/equipment','POST',equipment,t=reader_token,expected=403)
   call('Maintenance',base+'/equipment','POST',{**equipment,'status':'Fabricated'},expected=400)
   call('Maintenance',base+'/equipment','POST',{**equipment,'name':'   '},expected=400)
   call('Maintenance',base+'/equipment?pageSize=0',expected=400)
   invalid=call('Maintenance',base+'/equipment','POST',{},expected=400);assert invalid['statusCode']==400 and 'errors' in invalid and invalid['messageLevel']=='Cif'
   equipment['name']='Updated boiler';payload('Maintenance',base+'/equipment/'+eq,'PUT',equipment)
   assert payload('Maintenance',base+'/equipment?category=Heating')['totalCount']==1
   call('Maintenance',f'api/properties/{p2}/equipment/{eq}',t=other_token,expected=404)
   room=payload('Property',base+'/rooms','POST',{'name':'Kitchen','type':'Kitchen','floorAreaSquareMetres':12},expected=201)['roomId']
   payload('Property',base+'/rooms/'+room,'PUT',{'name':'Main kitchen','type':'Kitchen','floor':'Ground','floorAreaSquareMetres':15})
   call('Property',base+'/rooms','POST',{'name':'No access','type':'Kitchen'},t=reader_token,expected=403)
   payload('Property',base+'/passport/profile','PUT',{'floorAreaSquareMetres':85.5})
   call('Property',base+'/passport/profile','PUT',{'floorAreaSquareMetres':-1},expected=400)
   event=payload('Property',base+'/events','POST',{'eventAt':'2026-01-01T10:00:00+03:30','title':'Roof inspection','cost':75,'currencyCode':'GBP'},expected=201)['eventId']
   call('Property',base+'/events','POST',{'eventAt':'2026-01-01T10:00:00Z','title':'Invalid cost','cost':75},expected=400)
   content=b'%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF\n'
   doc=upload(p,content,related={'equipmentId':eq})['payload'];did=doc['documentId']
   assert doc['sizeBytes']==len(content) and doc['sha256']==hashlib.sha256(content).hexdigest().upper()
   fileurl=base+'/documents/'+did+'/file'
   assert call('Document',fileurl,raw=True)==content
   assert call('Document',fileurl+'?preview=true',raw=True)==content
   call('Document',fileurl,t=other_token,expected=404)
   upload(p,b'<html>not a PDF</html>',expected=400)
   upload(p,content,related={'equipmentId':str(uuid.uuid4())},expected=404)
   upload(p,content,t=reader_token,expected=403)
   assert payload('Document',base+'/documents?equipmentId='+eq)['totalCount']==1
   payload('Document',base+'/documents/'+did,'PUT',{'title':'Updated certificate','type':'Certificate','equipmentId':eq})
   # Reload both services: DB records and file bytes must survive process restart.
   for n in ['Maintenance','Document']:stop(n);start(n,env,folder)
   assert payload('Maintenance',base+'/equipment/'+eq)['details']['name']=='Updated boiler'
   assert call('Document',fileurl,raw=True)==content
   request=str(uuid.uuid4());hist=str(uuid.uuid4())
   sql(f"USE zumaris_service_request; INSERT INTO service_requests(id,user_id,property_id,category_id,area_type_id,description,urgency,time_zone_id,status,created_at,request_hash) VALUES('{request}','{user}','{p}',1,1,'A sufficiently detailed repair request.','Emergency','Europe/London','Pending',UTC_TIMESTAMP(6),REPEAT('a',64)); INSERT INTO service_request_status_history(id,request_id,to_status,actor_user_id,occurred_at)VALUES('{hist}','{request}','Pending','{user}',UTC_TIMESTAMP(6));")
   equipment['serviceRequestIds']=[request];linked=payload('Maintenance',base+'/equipment/'+eq,'PUT',equipment);assert linked['serviceRequestUrls']==['/service-request/api/service-requests/'+request]
   equipment['serviceRequestIds']=[str(uuid.uuid4())];call('Maintenance',base+'/equipment/'+eq,'PUT',equipment,expected=404)
   upload(p,content,related={'requestId':request})
   summary=payload('ServiceRequest',base+'/service-summary');assert summary=={'total':1,'open':1,'upcoming':0,'completed':0,'cancelled':0},summary
   metrics=payload('Property',base+'/passport/metrics');assert metrics['equipmentCount']==1 and metrics['documentCount']==2 and metrics['roomCount']==1 and metrics['healthScore'] is None and metrics['floorAreaSquareMetres']==85.5,metrics
   timeline=payload('Property',base+'/timeline');assert {e['source'] for e in timeline['items']}=={'Equipment','Document','Manual','ServiceRequest'},timeline
   assert payload('Property',base+'/timeline?source=Manual')['totalCount']==1
   assert payload('Property',base+'/timeline?type=DocumentUploaded')['totalCount']==2
   assert payload('Property',base+'/timeline?source=Manual&from=2026-01-01T06:00:00Z&to=2026-01-01T07:00:00Z')['totalCount']==1
   page1=payload('Property',base+'/timeline?pageSize=2');page2=payload('Property',base+'/timeline?pageNumber=2&pageSize=2');assert not({e['eventId'] for e in page1['items']}&{e['eventId'] for e in page2['items']})
   # Gateway exposes both newly activated services.
   assert payload('Gateway','maintenance/'+base+'/equipment')['totalCount']==1
   assert call('Gateway','document/'+fileurl,raw=True)==content
   revoked.add(owner_token);call('Document',fileurl,expected=401);revoked.clear()
   identity_status=503;call('Document',fileurl,expected=503);identity_status=204
   stop('Maintenance');call('Property',base+'/passport/metrics',expected=503);start('Maintenance',env,folder)
   call('Document',base+'/documents/'+did,'DELETE',expected=204)
   assert payload('Document',base+'/documents')['totalCount']==1
   assert payload('Document',base+'/documents?archived=true')['totalCount']==1
   assert call('Document',fileurl,raw=True)==content
   call('Document',base+'/documents/'+did,'PUT',{'title':'Archived','type':'Certificate'},expected=409)
   call('Maintenance',base+'/equipment/'+eq,'DELETE',expected=204)
   call('Property',base+'/rooms/'+room,'DELETE',expected=204)
   call('Property',base+'/events/'+event,'DELETE',expected=204)
   assert payload('Property',base+'/timeline?source=Manual')['totalCount']==0
   metrics=payload('Property',base+'/passport/metrics');assert metrics['equipmentCount']==0 and metrics['documentCount']==1 and metrics['roomCount']==0
   for n in list(processes):stop(n)
  print(f'PASS: {tests} HTTP checks; persistence/restart, real MySQL, authorization, file bytes, aggregate metrics/timeline, gateway, revocation and dependency failures.')
 finally:
  for n in list(processes):stop(n)
  server.shutdown();subprocess.run(['docker','rm','-f',container],capture_output=True)
