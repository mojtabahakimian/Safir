const { chromium } = require('@playwright/test');
const fs=require('fs'),path=require('path');
const BASE='https://127.0.0.1:7026', SHOTS=path.join(__dirname,'shots');
if(!fs.existsSync(SHOTS)) fs.mkdirSync(SHOTS);
function fc(){const r=process.env.PLAYWRIGHT_BROWSERS_PATH;
 for(const d of fs.readdirSync(r).filter(x=>x.startsWith('chromium-')).sort().reverse())
  for(const rel of ['chrome-linux/chrome','chrome-linux/headless_shell']){const p=path.join(r,d,rel);if(fs.existsSync(p))return p;}}
async function open(){
 const b=await chromium.launch({executablePath:fc()});
 const c=await b.newContext({ignoreHTTPSErrors:true,locale:'fa-IR',timezoneId:'Asia/Tehran',viewport:{width:1500,height:950}});
 const page=await c.newPage();
 const shot=async n=>{await page.screenshot({path:path.join(SHOTS,n+'.png')});console.log('📸',n);};
 const field=l=>page.locator('.mud-input-control').filter({has:page.locator('label',{hasText:l})}).locator('input').first();
 await page.goto(BASE+'/login',{waitUntil:'networkidle',timeout:120000});
 await page.waitForTimeout(2500);
 await field('نام کاربری').fill('payadmin'); await field('رمز عبور').fill('111111');
 await page.getByRole('button',{name:'ورود',exact:true}).click();
 await page.waitForURL(u=>!/\/login/.test(u.toString()),{timeout:90000});
 await page.getByText('حقوق و دستمزد',{exact:true}).last().click();
 await page.waitForTimeout(6000);
 return {b,page,shot,field};
}
module.exports={open};
