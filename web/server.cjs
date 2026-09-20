'use strict';
const http=require('node:http'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const validate=require('./validation.cjs');
const dir=path.resolve(process.env.DATA_DIR||path.join(__dirname,'private'));
const port=Number(process.env.PORT||18765),host=process.env.HOST||'127.0.0.1';
const origin=new URL(process.env.APP_ORIGIN||`http://127.0.0.1:${port}`).origin;
if(process.env.NODE_ENV==='production'&&!origin.startsWith('https://'))throw Error('生产环境必须设置 HTTPS APP_ORIGIN');
const config=JSON.parse(fs.readFileSync(path.join(dir,'auth.json'),'utf8'));
const dataFile=path.join(dir,'data.json');
const today=new Date().toISOString().slice(0,10),end=new Date();end.setUTCMonth(end.getUTCMonth()+6);
const initial={version:1,start:today,end:end.toISOString().slice(0,10),accounts:['现金账户','支付账户','储蓄账户','基金账户','银行卡一','银行卡二'].map(name=>({name,opening:0,reserve:0})),entries:[],goals:[],habits:['阅读','运动'],checks:{},plans:{},budget:0,partnerIncome:[],fundDays:{},balanceAdjustments:[]};
let state=validate(fs.existsSync(dataFile)?JSON.parse(fs.readFileSync(dataFile,'utf8')):initial);
if(!fs.existsSync(dataFile))fs.writeFileSync(dataFile,JSON.stringify(state),{mode:0o600});
const etag=()=> '"'+crypto.createHash('sha256').update(JSON.stringify(state)).digest('hex')+'"';
const sessions=new Map(),attempts=new Map();let hashing=false;
setInterval(()=>{const now=Date.now();for(const [k,s]of sessions)if(s.exp<now)sessions.delete(k);for(const [k,a]of attempts)if(a.until<now)attempts.delete(k)},60000).unref();
function body(req,max=2*1024*1024){return new Promise((resolve,reject)=>{let chunks=[],size=0;req.on('data',c=>{size+=c.length;if(size>max){chunks=[];reject(Object.assign(Error('内容过大'),{status:413}));}else chunks.push(c)});req.on('end',()=>{if(size<=max)try{resolve(JSON.parse(Buffer.concat(chunks).toString()))}catch{reject(Object.assign(Error('请求格式错误'),{status:400}))}});req.on('error',reject)})}
function save(next){validate(next);const backupDir=path.join(dir,'backups');fs.mkdirSync(backupDir,{recursive:true});fs.copyFileSync(dataFile,path.join(backupDir,`${Date.now()}-${crypto.randomBytes(4).toString('hex')}.json`));fs.writeFileSync(dataFile+'.tmp',JSON.stringify(next,null,2),{mode:0o600});fs.renameSync(dataFile+'.tmp',dataFile);state=next;const files=fs.readdirSync(backupDir).filter(x=>/^\d+-[a-f0-9]+\.json$/.test(x)).sort();for(const f of files.slice(0,-30))fs.unlinkSync(path.join(backupDir,f));}
const server=http.createServer(async(req,res)=>{
 const send=(status,obj)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'});res.end(JSON.stringify(obj))};
 res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('X-Frame-Options','DENY');res.setHeader('Referrer-Policy','same-origin');
 res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
 if(origin.startsWith('https://'))res.setHeader('Strict-Transport-Security','max-age=31536000');
 try{
 if(req.headers.host!==new URL(origin).host)return send(403,{error:'访问域名不匹配'});
 const url=new URL(req.url,origin).pathname;
 if(req.method==='GET'&&url==='/health')return send(200,{ok:true});
 const assets={'/icon.svg':['icon.svg','image/svg+xml'],'/favicon.ico':['zhiwei.ico','image/x-icon'],'/login':['login.html','text/html; charset=utf-8'],'/login.js':['login.js','text/javascript; charset=utf-8']};
 if(req.method==='GET'&&assets[url]){res.setHeader('Content-Type',assets[url][1]);return res.end(fs.readFileSync(path.join(__dirname,'public',assets[url][0])))}
 if(req.method==='POST'&&req.headers.origin!==origin)return send(403,{error:'请求来源不匹配'});
 if(req.method==='POST'&&url==='/login'){
  const key=req.socket.remoteAddress;let a=attempts.get(key);if(!a||a.until<Date.now()){a={count:0,until:Date.now()+15*60000};attempts.set(key,a)}
  if(a.count>=10)return send(429,{error:'尝试次数过多，请 15 分钟后重试'});
  if(hashing)return send(429,{error:'请稍后重试'});a.count++;
  const input=await body(req,4096);if(typeof input.password!=='string'||input.password.length>256)return send(400,{error:'密码格式错误'});
  if(hashing)return send(429,{error:'请稍后重试'});hashing=true;let hash;try{hash=await new Promise((resolve,reject)=>crypto.scrypt(input.password,config.salt,64,(e,h)=>e?reject(e):resolve(h)))}finally{hashing=false}
  if(!crypto.timingSafeEqual(hash,Buffer.from(config.hash,'hex')))return send(401,{error:'密码不正确'});
  attempts.delete(key);const sid=crypto.randomBytes(32).toString('hex');const csrf=crypto.randomBytes(24).toString('hex');
  if(sessions.size>=100)sessions.delete(sessions.keys().next().value);sessions.set(sid,{csrf,exp:Date.now()+8*3600000});
  res.setHeader('Set-Cookie',`zhiwei_session=${sid}; HttpOnly; SameSite=Strict; Path=/; Max-Age=28800${origin.startsWith('https://')?'; Secure':''}`);return send(200,{ok:true});
 }
 const sid=(req.headers.cookie||'').split(';').map(x=>x.trim()).find(x=>x.startsWith('zhiwei_session='))?.slice(15);
 const session=sessions.get(sid);if(!session||session.exp<Date.now()){if(req.method==='GET'&&url==='/'){res.writeHead(302,{Location:'/login'});return res.end()}return send(401,{error:'请先登录'})}
 if(req.method==='GET'&&url==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');return res.end(fs.readFileSync(path.join(__dirname,'public/index.html'),'utf8').replace('__TOKEN__',session.csrf))}
 if(req.method==='GET'&&url==='/session.js'){res.setHeader('Content-Type','text/javascript; charset=utf-8');return res.end(fs.readFileSync(path.join(__dirname,'public/session.js')))}
 if(req.headers['x-token']!==session.csrf)return send(403,{error:'会话校验失败，请刷新页面'});
 if(req.method==='POST'&&url==='/logout'){sessions.delete(sid);res.setHeader('Set-Cookie','zhiwei_session=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0'+(origin.startsWith('https://')?'; Secure':''));return send(200,{ok:true})}
 if(req.method==='GET'&&url==='/api'){res.setHeader('ETag',etag());return send(200,state)}
 if(req.method==='POST'&&url==='/api'){const next=await body(req);if(req.headers['if-match']!==etag())return send(409,{error:'另一窗口已修改数据，请刷新后重试'});try{validate(next)}catch{return send(400,{error:'数据校验失败，请检查金额、日期与记录格式'})}save(next);res.setHeader('ETag',etag());return send(200,{ok:true})}
 return send(404,{error:'页面不存在'});
 }catch(e){console.error(new Date().toISOString(),e.message);if(!res.headersSent)send(e.status||500,{error:e.status?e.message:'服务暂时异常，请稍后重试'});else res.end()}
});
server.requestTimeout=30000;server.headersTimeout=15000;server.listen(port,host,()=>console.log(`知微墨序已启动：${origin}`));
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>server.close(()=>process.exit(0)));
