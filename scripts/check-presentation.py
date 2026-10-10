"""Check real presentation hosts with a simulated API; no backend/database writes.

Requires Python 3 and a Debug build of NexoRuta.sln. Test ports must be free.
Logs and normalized snapshots are written outside the repository.
"""
import os,time,json,threading,subprocess,hashlib,re,socket,tempfile
from pathlib import Path
from http.server import ThreadingHTTPServer,BaseHTTPRequestHandler
from html.parser import HTMLParser
from urllib import request,parse,error
from http.cookiejar import CookieJar
root=Path(__file__).resolve().parent.parent; out=Path(tempfile.mkdtemp(prefix='nexoruta-presentation-')); stage='check'
ACCESS='00000000-0000-0000-0000-000000000005'; WRONG='00000000-0000-0000-0000-000000000006'; OP='00000000-0000-0000-0000-000000000002'
class Page(HTMLParser):
 def __init__(self):super().__init__();self.tokens={};self.elements=[];self.text=[];self.css=[];self.ignore=0
 def handle_starttag(self,tag,attrs):
  attrs=dict(attrs)
  if tag in ['script','style']:self.ignore+=1
  if tag=='link' and attrs.get('rel')=='stylesheet':self.css.append(attrs['href'])
  if tag=='input' and attrs.get('name')=='__RequestVerificationToken':self.tokens['token']=attrs['value']
  if tag in ['form','input','select','button','fieldset','a','link']:
   self.elements.append([tag,{k:v for k,v in attrs.items() if not(tag=='input' and attrs.get('name')=='__RequestVerificationToken' and k=='value')}])
 def handle_endtag(self,tag):
  if tag in ['script','style']:self.ignore-=1
 def handle_data(self,data):
  if data.strip() and not self.ignore:self.text.append(re.sub(r'\b(?:00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}|[0-9A-Z]{13}:[0-9]{8})\b','REQUEST_ID',data.strip()))
class NoRedirect(request.HTTPRedirectHandler):
 def redirect_request(self,*args):return None
all_results={}
for app,port in [('Backoffice',55141),('Commerce',55140),('Tracking',55142)]:
 with socket.socket() as check:check.bind(('127.0.0.1',port))
 calls=[];failures=[];expected_type='Operador' if app=='Backoffice' else 'Comercio'
 def user(kind,access=ACCESS):return {'accesoId':access,'usuarioId':'00000000-0000-0000-0000-000000000001','operadorId':OP if kind=='Operador' else None,'comercioId':None if kind=='Operador' else '00000000-0000-0000-0000-000000000003','usuarioEmail':'usuario@presentation.test','operadorNombre':'Operador de prueba' if kind=='Operador' else None,'comercioNombre':None if kind=='Operador' else 'Comercio de prueba','tipo':kind,'esPropietario':kind=='Comercio'}
 class Api(BaseHTTPRequestHandler):
  def do_GET(self):
   calls.append(self.path);access=self.headers.get('X-NexoRuta-Acceso')
   if self.path=='/api/accesos?tipo='+expected_type:
    if access is not None:failures.append('Access listing has unexpected header')
    data=[user(expected_type)]
   elif self.path=='/api/usuarios/actual':
    if access not in [ACCESS,WRONG]:failures.append('Current user missing access')
    data=user(expected_type if access==ACCESS else ('Comercio' if expected_type=='Operador' else 'Operador'),access)
   elif self.path=='/api/comercio/operadores':
    if access!=ACCESS:failures.append('Operators access differs')
    data=[{'operadorId':OP,'nombre':'Operador de prueba'}]
   elif self.path=='/api/envios':
    if access!=ACCESS:failures.append('Shipments access differs')
    data=[{'id':'00000000-0000-0000-0000-000000000010','operadorId':OP,'comercioId':'00000000-0000-0000-0000-000000000003','creadoPorUsuarioId':'00000000-0000-0000-0000-000000000001','usuarioEmail':'usuario@presentation.test','operadorNombre':'Operador de prueba','comercioNombre':'Comercio de prueba','destinatarioNombre':'Destinatario de prueba','direccion':'Direccion de prueba','estado':'Admitido','bultos':[{'codigo':'P-TEST','pesoGramos':1250,'largoCentimetros':30,'anchoCentimetros':20,'altoCentimetros':10}]}]
   else:failures.append('Unexpected API route');self.send_error(404);return
   body=json.dumps(data).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
  def log_message(self,*args):pass
 api=ThreadingHTTPServer(('127.0.0.1',0),Api);threading.Thread(target=api.serve_forever,daemon=True).start()
 env=os.environ.copy();env['Api__BaseAddress']=f'http://127.0.0.1:{api.server_port}/';env['DOTNET_ENVIRONMENT']=env['ASPNETCORE_ENVIRONMENT']='Development'
 log=open(out/(stage+'-'+app+'.log'),'w',encoding='utf-8')
 process=subprocess.Popen(['dotnet','run','--project',f'src/NexoRuta.{app}/NexoRuta.{app}.csproj','--no-build','--no-launch-profile','--urls',f'http://127.0.0.1:{port}'],env=env,cwd=root,stdout=log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
 base=f'http://127.0.0.1:{port}';jar=CookieJar();opener=request.build_opener(request.HTTPCookieProcessor(jar),NoRedirect());results={}
 def fetch(path,data=None,label=None,record=True):
  try:response=opener.open(request.Request(base+path,data=data),timeout=10)
  except error.HTTPError as e:response=e
  body=response.read();page=Page();page.feed(body.decode('utf-8',errors='replace'))
  if record:
   cookies=[]
   for header in response.headers.get_all('Set-Cookie',[]):
    parts=header.split(';');cookies.append([parts[0].split('=')[0],sorted(part.strip() for part in parts[1:] if not part.strip().lower().startswith(('expires=','max-age=')))])
   results[label or path]={'status':response.code,'location':response.headers.get('Location','').replace(base,'http://app.test'),'text':page.text,'elements':page.elements,'cookies':cookies,'content_type':response.headers.get('Content-Type','')}
  return response.code,response.headers,page,body
 try:
  for i in range(80):
   if process.poll() is not None:raise AssertionError(app+' process exited; inspect bounded local log')
   try:
    if fetch('/',record=False)[0] in [200,302]:break
   except (error.URLError,TimeoutError):pass
   time.sleep(.25)
  else:raise AssertionError(app+' startup timeout')
  if app=='Tracking':
   for path in ['/','/Error','/not-found','/arq05-missing','/health/ready']:
    code,_,page,_=fetch(path)
    if path=='/':assert code==200 and 'Hello, world!' in page.text
    else:assert code==(200 if path in ['/Error','/not-found'] else 404)
   for path in ['/app.css','/lib/bootstrap/dist/css/bootstrap.min.css','/favicon.png']:
    code,_,_,body=fetch(path,record=False);assert code==200;results[path]={'status':code,'sha256':hashlib.sha256(body).hexdigest()}
   assert not calls,'Tracking unexpectedly invokes an API'
  else:
   code,headers,_,_=fetch('/',label='anonymous_root');assert code==302 and '/ingresar' in headers['Location']
   if app=='Backoffice':
    code,_,_,_=fetch('/Index',label='anonymous_Index');assert code==302
   code,_,login,_=fetch('/ingresar');assert code==200 and 'token' in login.tokens
   previous_calls=len(calls)
   code,headers,_,_=fetch('/ingresar',parse.urlencode({'AccesoId':ACCESS}).encode(),label='login_no_antiforgery');assert code==400 or (app=='Commerce' and code==302 and '/ingresar' in headers.get('Location',''))
   assert len(calls)==previous_calls, 'Login handler ran without antiforgery'
   post=lambda access:parse.urlencode({'AccesoId':access,'__RequestVerificationToken':login.tokens['token']}).encode()
   code,_,invalid,_=fetch('/ingresar',post(''),label='login_missing_access');assert code==200 and 'Seleccioná un usuario.' in invalid.text
   code,_,wrong,_=fetch('/ingresar',post(WRONG),label='login_wrong_account');assert code==200 and any('no puede ingresar' in text for text in wrong.text)
   code,headers,_,_=fetch('/ingresar',post(ACCESS),label='login_valid');assert code==302 and headers['Location']=='/'
   code,_,home,body=fetch('/',label='authenticated_root');assert code==200 and 'token' in home.tokens
   if app=='Backoffice':
    assert 'Envíos recibidos' in home.text and 'Destinatario de prueba' in home.text
    code,_,_,_=fetch('/Index',label='authenticated_Index');assert code==200
   else:assert 'Alta de envío' in home.text and '<!--Blazor:' in body.decode()
   for path in ['/Error','/not-found','/arq05-missing','/health/ready']:
    code,_,_,_=fetch(path,label='authenticated'+path)
    assert code==(200 if path in ['/Error','/health/ready'] or app=='Commerce' and path=='/not-found' else 404)
   for path in home.css:
    if path.startswith(('http:','https:')):continue
    code,_,_,body=fetch('/'+path.lstrip('/'),record=False);assert code==200;results['asset:'+path]={'status':code,'sha256':hashlib.sha256(body).hexdigest()}
   code,headers,_,_=fetch('/salir',label='logout_get');assert code==302 and headers['Location']=='/'
   code,_,_,_=fetch('/',record=False);assert code==200
   code,_,_,_=fetch('/salir',b'',label='logout_no_antiforgery');assert code in [400,404]
   code,_,_,_=fetch('/',record=False);assert code==200
   post= parse.urlencode({'__RequestVerificationToken':home.tokens['token']}).encode()
   code,headers,_,_=fetch('/salir',post,label='logout_valid');assert code==302 and headers['Location']=='/ingresar'
   code,_,_,_=fetch('/',label='root_after_logout');assert code==302
   assert not failures,failures
  results['api_calls']=calls;all_results[app]=results
  print(stage+' '+app+': '+str(len(results)-1)+' HTTP/asset scenarios PASS; real app, simulated API, no database.')
 finally:
  process.terminate()
  try:process.wait(timeout=15)
  except subprocess.TimeoutExpired:process.kill();process.wait()
  log.close();api.shutdown();api.server_close()
(out/(stage+'-http.json')).write_text(json.dumps(all_results,ensure_ascii=False,indent=2),encoding='utf-8')
