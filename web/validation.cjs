'use strict';
const MX_SEED={Version:1,Days:[]};
function normalize(s){s.partnerIncome??=[];s.fundDays??={};s.balanceAdjustments??=[];return s}
function balances(s){let b=s.accounts.map(a=>a.opening);for(const e of s.entries){b[e.account]+=(['收入','估值增加'].includes(e.type)?1:-1)*e.amount;if(e.type==='转账')b[e.to]+=e.amount}for(const a of s.balanceAdjustments||[])b[a.account]+=a.amount;return b}

function adjustBalance(s,account,actual,note=''){
 if(!Number.isInteger(account)||!s.accounts[account]||!Number.isSafeInteger(actual)||actual<0)throw Error('请填写有效的实际余额，不能小于 0');
 let before=balances(s)[account],delta=actual-before;
 if(!Number.isSafeInteger(delta))throw Error('金额超出范围');
 if(delta)(s.balanceAdjustments??=[]).push({id:uid(),date:today(),account,before,after:actual,amount:delta,note});
}
function validateAdjustments(s){
 if(!Array.isArray(s.balanceAdjustments))throw Error('余额校准记录格式不正确');
 const ids=new Set();for(const a of s.balanceAdjustments){if(!a||typeof a.id!=='string'||!a.id||ids.has(a.id)||!validDate(a.date)||!Number.isInteger(a.account)||!s.accounts[a.account]||![a.before,a.after,a.amount].every(Number.isSafeInteger)||a.before<0||a.after<0||a.after-a.before!==a.amount||typeof a.note!=='string')throw Error('余额校准记录格式不正确');ids.add(a.id)}
}

function reserves(s){return s.accounts.map((a,i)=>a.reserve-s.entries.filter(e=>e.account===i).reduce((n,e)=>n+(e.reserveRelease||0),0))}
function stats(es){return {income:es.filter(e=>e.type==='收入').reduce((a,e)=>a+e.amount,0),expense:es.filter(e=>e.type==='支出').reduce((a,e)=>a+e.amount,0)}}
function earned(e){return e.type==='收入'&&!['生活费','退款','利息'].includes(e.category)&&(e.earned===true||(e.earned===undefined&&earningCats.includes(e.category)))}
function ownIncome(s){return s.entries.filter(e=>earned(e)&&e.date>=s.start&&e.date<=s.end)}
function partnerIncome(s){return s.partnerIncome.filter(e=>e.date>=s.start&&e.date<=s.end)}
function validDate(d){return /^\d{4}-\d{2}-\d{2}$/.test(d)&&!isNaN(new Date(d+'T12:00:00'))&&new Date(d+'T12:00:00').toISOString().slice(0,10)===d}
function validate(s){
 if(!s||s.version!==1||!Array.isArray(s.accounts)||s.accounts.length!==6||!Array.isArray(s.entries)||!Array.isArray(s.goals)||!Array.isArray(s.habits)||!s.plans||!s.checks)throw Error('不是有效的知微备份');
 if(!validDate(s.start)||!validDate(s.end)||s.end<=s.start||!Number.isSafeInteger(s.budget)||s.budget<0)throw Error('日期或预算格式不正确');
 for(const a of s.accounts)if(typeof a.name!=='string'||!Number.isSafeInteger(a.opening)||!Number.isSafeInteger(a.reserve)||a.reserve<0)throw Error('账户格式不正确');
 const ids=new Set();for(const e of s.entries){if(!/^[a-zA-Z0-9-]+$/.test(e.id)||ids.has(e.id)||!validDate(e.date)||!Number.isSafeInteger(e.amount)||e.amount<=0||!Number.isInteger(e.account)||!s.accounts[e.account]||!['支出','收入','转账','估值增加','估值减少'].includes(e.type)||typeof e.category!=='string'||typeof e.note!=='string')throw Error('流水格式不正确');ids.add(e.id);if(e.type==='转账'&&(!Number.isInteger(e.to)||!s.accounts[e.to]||e.to===e.account))throw Error('转账账户不正确');if(e.reserveRelease!==undefined&&(!Number.isSafeInteger(e.reserveRelease)||e.reserveRelease<0||e.reserveRelease>e.amount||(e.reserveRelease>0&&!['支出','转账'].includes(e.type))))throw Error('专款使用金额不正确')}
 for(const g of s.goals)if(!g||typeof g.name!=='string'||typeof g.unit!=='string'||!['我','男朋友'].includes(g.who)||!Number.isFinite(g.target)||g.target<=0||!Number.isFinite(g.value)||g.value<0)throw Error('目标格式不正确');
 if(s.habits.some(h=>typeof h!=='string')||Object.entries(s.plans).some(([k,v])=>!/^\d{4}-\d{2}$/.test(k)||!Number.isSafeInteger(v)||v<0))throw Error('计划格式不正确');
 if(!Array.isArray(s.partnerIncome)||s.partnerIncome.some(e=>!e||!/^[a-zA-Z0-9-]+$/.test(e.id)||!validDate(e.date)||!Number.isSafeInteger(e.amount)||e.amount<=0||typeof e.note!=='string'))throw Error('男朋友收入格式不正确');
 if(!s.fundDays||Array.isArray(s.fundDays)||typeof s.fundDays!=='object'||Object.entries(s.fundDays).some(([d,v])=>!validDate(d)||v!==true))throw Error('基金记录格式不正确');
 validateAdjustments(s);
 if(balances(s).some(n=>!Number.isSafeInteger(n)||n<0))throw Error('变更后账户余额不足，请核对相关流水');if(reserves(s).some(n=>!Number.isSafeInteger(n)||n<0))throw Error('专款预留不足，请先调整预留');return s
}

const mxNormalizeBase=normalize;normalize=function(s){mxNormalizeBase(s);s.moxu??=structuredClone(MX_SEED);return s};
const mxValidateBase=validate;validate=function(s){mxValidateBase(s);if(!s.moxu||s.moxu.Version!==1||!Array.isArray(s.moxu.Days))throw Error('墨序数据格式不正确');const dates=new Set();for(const d of s.moxu.Days){if(!validDate(d.Date)||dates.has(d.Date)||typeof d.Journal!=='string'||!Array.isArray(d.Items))throw Error('墨序日期或随记格式不正确');dates.add(d.Date);const ids=new Set();for(const t of d.Items){if(typeof t.Id!=='string'||!t.Id||ids.has(t.Id)||typeof t.Title!=='string'||typeof t.Time!=='string'||typeof t.Note!=='string'||typeof t.Done!=='boolean'||typeof t.Important!=='boolean')throw Error('墨序安排格式不正确');ids.add(t.Id)}}return s};

module.exports=s=>validate(normalize(s));
