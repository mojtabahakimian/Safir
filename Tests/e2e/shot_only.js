const {open}=require('./lib');
(async()=>{const {b,page,shot}=await open();
 await page.getByText('محاسبه حقوق',{exact:true}).first().click(); await page.waitForTimeout(6000);
 await page.getByRole('button',{name:/ورود به میز کار محاسباتی/}).first().click(); await page.waitForTimeout(9000);
 await shot('23a-state');
 console.log('دکمه‌ها:',JSON.stringify([...new Set((await page.getByRole('button').allTextContents()).map(s=>s.trim().replace(/\s+/g,' ')).filter(Boolean))]));
 await b.close();})().catch(e=>{console.error('❌',e.message.slice(0,150));process.exit(1);});
