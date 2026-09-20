const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),{spawn,execFileSync}=require('node:child_process');
const root=__dirname,dir=path.join(root,'private'),url='http://127.0.0.1:18765';
function ready(){return new Promise(resolve=>{const req=http.get(url+'/login',res=>{let body='';res.on('data',c=>body+=c);res.on('end',()=>resolve(res.statusCode===200&&body.includes('登录 · 知微墨序')))});req.setTimeout(1000,()=>req.destroy());req.on('error',()=>resolve(false))})}
(async()=>{
 fs.mkdirSync(dir,{recursive:true});
 if(!fs.existsSync(path.join(dir,'auth.json'))){const result=execFileSync(process.execPath,[path.join(root,'setup.cjs')],{cwd:root,env:{...process.env,DATA_DIR:dir},encoding:'utf8',windowsHide:true});fs.writeFileSync(path.join(dir,'首次登录.txt'),'本机演示地址：'+url+'\n\n'+result+'\n请妥善保存密码，不要将此文件上传至公开仓库。\n',{mode:0o600,flag:'wx'});}
 if(!await ready()){
  const log=fs.openSync(path.join(dir,'server.log'),'a');
  const child=spawn(process.execPath,[path.join(root,'server.cjs')],{cwd:root,env:{...process.env,DATA_DIR:dir,HOST:'127.0.0.1',PORT:'18765',APP_ORIGIN:url,NODE_ENV:'development'},detached:true,windowsHide:true,stdio:['ignore',log,log]});child.on('error',e=>console.error(e.message));child.unref();fs.closeSync(log);
  let running=false;for(let i=0;i<40;i++){if(await ready()){running=true;break}await new Promise(r=>setTimeout(r,100))}if(!running)throw Error('启动失败，请查看 private/server.log');
 }
 console.log('网站已经启动：'+url+'\n首次登录密码保存在 private/首次登录.txt。\n关闭此窗口后，网站仍会在后台运行。');
})().catch(e=>{console.error(e.message);process.exitCode=1});
