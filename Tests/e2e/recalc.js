const {open}=require('./lib');
(async()=>{
 const {b,page,shot}=await open();
 await page.getByText('محاسبه حقوق',{exact:true}).first().click();
 await page.waitForTimeout(6000);
 await page.getByRole('button',{name:/ورود به میز کار محاسباتی/}).first().click();
 await page.waitForTimeout(9000);
 const undo = page.getByRole('button',{name:/لغو صدور سند/}).first();
 if (await undo.count()>0){ console.log('→ لغو صدور سند'); await undo.click(); await page.waitForTimeout(3000);
   const y=page.getByRole('button',{name:/^(بله)/}); if(await y.count()>0){await y.first().click();} await page.waitForTimeout(10000);}
 const unfin = page.getByRole('button',{name:/لغو تأیید/}).first();
 if (await unfin.count()>0){ console.log('→ لغو تأیید'); await unfin.click(); await page.waitForTimeout(3000);
   const y=page.getByRole('button',{name:/^(بله)/}); if(await y.count()>0){await y.first().click();} await page.waitForTimeout(10000);}
 console.log('→ بازمحاسبه حقوق');
 const re = page.getByRole('button',{name:/بازمحاسبه حقوق|اجرای موتور محاسبه/}).first();
 await re.click(); await page.waitForTimeout(3500);
 const y2=page.getByRole('button',{name:/^(بله|تأیید|تایید)/}); if(await y2.count()>0){console.log('→ تأیید:',(await y2.first().innerText()).trim()); await y2.first().click();}
 await page.waitForTimeout(20000);
 await shot('23-recalc-with-bonus');
 const rows = await page.evaluate(()=>{
   const heads=[...document.querySelectorAll('.e-gridheader th,.e-headercell')].map(h=>h.innerText.trim()).filter(Boolean);
   const trs=[...document.querySelectorAll('.e-gridcontent tr,tbody tr')];
   return {heads, rows: trs.map(tr=>[...tr.querySelectorAll('td')].map(td=>td.innerText.trim())).filter(r=>r.length)};
 });
 console.log('HEADERS:',JSON.stringify(rows.heads));
 rows.rows.forEach(r=>console.log('ROW:',JSON.stringify(r)));
 await b.close();
})().catch(e=>{console.error('❌',e.message);process.exit(1);});
