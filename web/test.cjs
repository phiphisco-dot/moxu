const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process'),{once}=require('node:events');
test('网页脚本可加载，六个页面能渲染，演示种子为空',async()=>{
 const vm=require('node:vm'),html=fs.readFileSync(path.join(__dirname,'public/index.html'),'utf8'),script=html.match(/<script>'use strict';([\s\S]*)<\/script>/)[1];
 assert.match(html,/const MX_SEED=\{Version:1,Days:\[\]\};/);
 const nodes=new Map();const element=()=>({innerHTML:'',textContent:'',hidden:false,style:{},insertAdjacentHTML(position,html){this.innerHTML+=html},classList:{add(){},remove(){},toggle(){}},querySelector(){return element()},querySelectorAll(){return []},addEventListener(){},setAttribute(){},focus(){}});
 const state=require('./validation.cjs')({version:1,start:'2026-09-20',end:'2027-03-20',accounts:Array.from({length:6},(_,i)=>({name:'演示账户'+i,opening:0,reserve:0})),entries:[],goals:[],habits:[],checks:{},plans:{},budget:0});
 const context={console,URL,Blob,crypto:crypto.webcrypto,structuredClone,Map,Set,Date,JSON,location:{hash:'',href:''},localStorage:{getItem(){return null},setItem(){},removeItem(){}},document:{hidden:false,body:element(),documentElement:element(),addEventListener(){},querySelector(s){if(!nodes.has(s))nodes.set(s,element());return nodes.get(s)}},fetch:async()=>new Response(JSON.stringify(state),{headers:{ETag:'"demo"'}}),setTimeout(){},clearTimeout(){},setInterval(){},alert(message){throw Error(message)}};
 context.window=context;context.addEventListener=()=>{};vm.createContext(context);vm.runInContext(script,context);await new Promise(r=>setImmediate(r));await new Promise(r=>setImmediate(r));
 assert.ok(!nodes.get('main').innerHTML.includes('账本连接失败'),nodes.get('main').innerHTML);for(let i=0;i<=5;i++){vm.runInContext(`page=${i};render()`,context);assert.ok(nodes.get('main').innerHTML.length>0)}
});
test('登录、权限、数据校验、并发保存、备份及重启持久化',async()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'zhiwei-test-')),password=crypto.randomBytes(24).toString('hex'),salt=crypto.randomBytes(24).toString('hex');
 fs.writeFileSync(path.join(dir,'auth.json'),JSON.stringify({salt,hash:crypto.scryptSync(password,salt,64).toString('hex')}));
 const net=require('node:net'),probe=net.createServer();await new Promise(r=>probe.listen(0,'127.0.0.1',r));const port=probe.address().port;await new Promise(r=>probe.close(r));
 const base=`http://127.0.0.1:${port}`;let child;
 async function start(){child=spawn(process.execPath,['server.cjs'],{cwd:__dirname,env:{...process.env,DATA_DIR:dir,PORT:String(port),HOST:'127.0.0.1',APP_ORIGIN:base,NODE_ENV:'test'},stdio:['ignore','pipe','pipe'],windowsHide:true});let errors='';child.stderr.on('data',x=>errors+=x);for(let i=0;i<100;i++){if(child.exitCode!==null)throw Error(errors);try{if((await fetch(base+'/health')).ok)return}catch{}await new Promise(r=>setTimeout(r,50))}throw Error('启动超时 '+errors)}
 async function stop(){if(child&&child.exitCode===null){const done=once(child,'exit');child.kill();await done}}
 const req=(url,options={})=>fetch(base+url,{redirect:'manual',...options});
 async function login(){const r=await req('/login',{method:'POST',headers:{Origin:base,'Content-Type':'application/json'},body:JSON.stringify({password})});assert.equal(r.status,200);assert.match(r.headers.get('set-cookie'),/HttpOnly; SameSite=Strict/);const cookie=r.headers.get('set-cookie').split(';')[0];const page=await req('/',{headers:{Cookie:cookie}});assert.equal(page.status,200);const html=await page.text(),csrf=html.match(/const token='([a-f0-9]+)'/)[1];return {Cookie:cookie,'x-token':csrf,Origin:base,'Content-Type':'application/json'}}
 try{
 await start();assert.equal((await req('/')).status,302);assert.equal((await req('/api')).status,401);assert.equal((await req('/private/auth.json')).status,401);
 assert.equal((await req('/login',{method:'POST',headers:{Origin:'https://evil.example'},body:'{}'})).status,403);
 assert.equal((await req('/login',{method:'POST',headers:{Origin:base},body:JSON.stringify({password:'wrong'})})).status,401);
 const headers=await login();let r=await req('/api',{headers});assert.equal(r.status,200);let state=await r.json(),revision=r.headers.get('etag');assert.ok(state.accounts.every(a=>a.opening===0&&a.reserve===0));assert.equal(state.entries.length,0);assert.equal(state.goals.length,0);
 assert.equal((await req('/api',{method:'POST',headers:{...headers,'x-token':'wrong','If-Match':revision},body:JSON.stringify(state)})).status,403);
 assert.equal((await req('/api',{method:'POST',headers:{...headers,'If-Match':revision},body:JSON.stringify({...state,budget:-1})})).status,400);
 assert.deepEqual(state.moxu,{Version:1,Days:[]});
 assert.equal((await req('/api',{method:'POST',headers:{...headers,'If-Match':revision},body:JSON.stringify({...state,moxu:{Version:1,Days:[{Date:state.start,Journal:123,Items:[]}]}})})).status,400);
 state.budget=123000;state.moxu.Days.push({Date:state.start,Journal:'演示日记',Items:[{Id:'demo-1',Title:'验证上线',Time:'',Note:'',Done:false,Important:false}]});r=await req('/api',{method:'POST',headers:{...headers,'If-Match':revision},body:JSON.stringify(state)});assert.equal(r.status,200);
 assert.equal((await req('/api',{method:'POST',headers:{...headers,'If-Match':revision},body:JSON.stringify({...state,budget:456000})})).status,409);
 const backups=fs.readdirSync(path.join(dir,'backups'));assert.equal(backups.length,1);assert.equal(JSON.parse(fs.readFileSync(path.join(dir,'backups',backups[0]))).budget,0);
 await stop();await start();assert.equal((await req('/api',{headers})).status,401);const fresh=await login();const restored=await(await req('/api',{headers:fresh})).json();assert.equal(restored.budget,123000);assert.equal(restored.moxu.Days[0].Journal,'演示日记');
 assert.equal((await req('/private/data.json',{headers:fresh})).status,404);
 assert.equal((await req('/logout',{method:'POST',headers:fresh})).status,200);assert.equal((await req('/api',{headers:fresh})).status,401);
 for(let i=0;i<10;i++)assert.equal((await req('/login',{method:'POST',headers:{Origin:base},body:JSON.stringify({password:'wrong'})})).status,401);
 assert.equal((await req('/login',{method:'POST',headers:{Origin:base},body:JSON.stringify({password:'wrong'})})).status,429);
 }finally{await stop();fs.rmSync(dir,{recursive:true,force:true})}
});


