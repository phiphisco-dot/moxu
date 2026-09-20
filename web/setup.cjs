const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const dir=path.resolve(process.env.DATA_DIR||path.join(__dirname,'private'));fs.mkdirSync(dir,{recursive:true,mode:0o700});
const file=path.join(dir,'auth.json');if(fs.existsSync(file)){console.error('已配置密码。为避免覆盖现有配置，初始化已停止。');process.exit(1)}
const password=crypto.randomBytes(18).toString('base64url'),salt=crypto.randomBytes(32).toString('hex');
fs.writeFileSync(file,JSON.stringify({salt,hash:crypto.scryptSync(password,salt,64).toString('hex')}),{mode:0o600,flag:'wx'});
console.log('请保存登录密码（仅本次显示）：\n'+password+'\n然后运行 node server.cjs');
