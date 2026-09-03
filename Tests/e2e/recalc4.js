const {open}=require('./lib');
(async()=>{
 const {b,page,shot}=await open();
 const killBanner = async () => page.evaluate(() => {
   document.querySelectorAll('div,iframe').forEach(el=>{
     const t=(el.innerText||'');
     if(/Syncfusion/i.test(t) && el.getBoundingClientRect().top < 120) el.remove();
   });
   document.querySelectorAll('[id*="js-licensing"],[class*="e-trial"],iframe').forEach(e=>e.remove());
 });
 await page.getByText('محاسبه حقوق',{exact:true}).first().click(); await page.waitForTimeout(6000);
 await page.getByRole('button',{name:/ورود به میز کار محاسباتی/}).first().click(); await page.waitForTimeout(9000);
 await killBanner();

 const btn = page.getByRole('button',{name:/اجرای موتور محاسبه|بازمحاسبه حقوق/}).first();
 console.log('→', (await btn.innerText()).trim());
 await btn.click();
 await page.waitForTimeout(3000);
 await killBanner();
 const dlg=page.locator('.mud-dialog').first();
 if(await dlg.count()>0){const y=dlg.getByRole('button',{name:/^(بله|تأیید|تایید)/}).first();
   if(await y.count()>0){console.log('   تأیید:',(await y.innerText()).trim()); await y.click();}}
 await page.waitForTimeout(22000);
 await killBanner();
 await shot('23-recalc-with-bonus');
 const rows=await page.evaluate(()=>{
  const heads=[...document.querySelectorAll('.e-gridheader th,.e-headercell')].map(h=>h.innerText.trim()).filter(Boolean);
  const trs=[...document.querySelectorAll('.e-gridcontent tr,tbody tr')];
  return {heads,rows:trs.map(tr=>[...tr.querySelectorAll('td')].map(td=>td.innerText.trim())).filter(r=>r.length)};
 });
 console.log('HEADERS:',JSON.stringify(rows.heads));
 rows.rows.forEach(r=>console.log('ROW:',JSON.stringify(r)));
 await b.close();
})().catch(e=>{console.error('❌',e.message.slice(0,200));process.exit(1);});
