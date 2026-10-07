const pw = require('/home/box/.local/lib/node_modules/playwright');
const fs = require('fs');
(async()=>{
 const b = await pw.chromium.launch({executablePath:'/usr/bin/google-chrome'});
 const ctx = await b.newContext({viewport:{width:1366,height:900}});
 const p = await ctx.newPage();
 await p.route(/google-analytics|googletagmanager|\/g\/collect|RegisterWithCaptcha|api\.dmca\.com\/login/, r=>r.abort());
 await p.goto('https://www.dmca.com/add/',{waitUntil:'load',timeout:90000});
 await p.waitForFunction(()=>window.DMCAUpgrade,null,{timeout:30000});
 await p.evaluate(()=>{window.DMCAUpgrade.open('x'); window.DMCAUpgrade.start({plan:'pro',cycle:'monthly',method:'credit',gesture:true});});
 await p.waitForTimeout(1000);
 const res = await p.evaluate(()=>{
  const m=document.querySelector('.proComparisonModal.plan-cards-layout');
  // make every state's elements present for matching
  const s=m.querySelector('.plan-auth-step');
  const pay=document.createElement('div'); pay.className='plan-pay'; pay.innerHTML='<div class="plan-pay__element"></div><p class="plan-auth__msg plan-auth__msg--error"></p><button class="btn plan-auth__submit plan-pay__submit"></button><p class="plan-auth__fine plan-pay__fine"></p><p class="plan-pay__alt"><a class="plan-pay__alt-link"></a></p>'; s.appendChild(pay);
  const roots=[m, document.querySelector('.redirectModalVeil')];
  const KEEP=/proComparisonModal|plan-auth|plan-pay|plan-card|plan-compare|plan-billing|plan-full-features|upgrade-mobile-nav|plan-tooltip|plan-modal-subtitle|redirectModalVeil/;
  const SKIP=/plan-modal-open|status-tier|tier-chip|js-tier|#dmca-plan|add-pay-toggle|\.add-plan|statusDetails|#subLogin/;
  function split(t){const a=[];let d=0,c='';for(const ch of t){if(ch==='('||ch==='[')d++;if(ch===')'||ch===']')d--;if(ch===','&&d===0){a.push(c.trim());c='';}else c+=ch;}if(c.trim())a.push(c.trim());return a;}
  function strip(sel){return sel.replace(/::?[a-zA-Z-]+(\((?:[^()]|\([^()]*\))*\))?/g,'').trim();}
  function scopeSel(sel){
    let s=sel.replace(/^:root\b/,'html');
    // drop leading html / body compounds
    s=s.replace(/^html(\[[^\]]*\]|\.[\w-]+)*\s*>?\s*/,'');
    s=s.replace(/^body(\[[^\]]*\]|\.[\w-]+)*\s*>?\s*/,'');
    if(!s) return null;
    return '#dmcaAcct '+s;
  }
  function matches(sel){
    let s=strip(sel).replace(/^html\b[^\s>]*\s*>?\s*/,'').replace(/^body\b[^\s>]*\s*>?\s*/,'').replace(/[\s>+~]+$/,'');
    if(!s) return false;
    try{for(const r of roots){if(!r)continue;if(r.matches(s)||r.querySelector(s))return true;}}catch(e){return false;}
    return false;
  }
  const out=[]; const kf=new Set(); const usedAnims=new Set(); const keyframes={};
  function walk(rules, media){
    for(const r of rules){
      if(r instanceof CSSStyleRule){
        const keep=split(r.selectorText).filter(x=>!SKIP.test(x)&&(KEEP.test(x)||matches(x))).map(scopeSel).filter(Boolean);
        if(keep.length){ out.push({media, text: keep.join(',\n')+' { '+r.style.cssText+' }'}); const an=r.style.animationName||''; (r.style.animation||'').split(/[ ,]/).concat(an.split(',')).forEach(x=>x&&usedAnims.add(x.trim())); }
      } else if(r instanceof CSSMediaRule){ walk(r.cssRules, media.concat([r.conditionText||r.media.mediaText])); }
      else if(r instanceof CSSSupportsRule){ walk(r.cssRules, media.concat(['@supports '+r.conditionText])); }
      else if(r instanceof CSSKeyframesRule){ keyframes[r.name]=r.cssText; }
    }
  }
  const sheets=[];
  for(const sh of document.styleSheets){ let rules; try{rules=sh.cssRules;}catch(e){sheets.push({href:sh.href,skipped:true});continue;} sheets.push({href:sh.href||'inline',n:rules.length}); walk(rules,[]); }
  const anims=[...usedAnims].filter(a=>keyframes[a]).map(a=>keyframes[a]);
  const bcs=getComputedStyle(document.body);
  const body={fontFamily:bcs.fontFamily,fontSize:bcs.fontSize,lineHeight:bcs.lineHeight,color:bcs.color,letterSpacing:bcs.letterSpacing,fontWeight:bcs.fontWeight,webkitFontSmoothing:bcs.webkitFontSmoothing};
  return {out,anims,sheets,body};
 });
 fs.writeFileSync(process.argv[2]||'www-modal-css.json', JSON.stringify(res,null,1));
 console.log(res.sheets, res.out.length, res.anims.length, res.body);
 await b.close();
})().catch(e=>{console.error(e);process.exit(1)});
