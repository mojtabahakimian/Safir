// آزمون طلایی دستیار هوش مصنوعی — هر سؤال را از /api/ai/chat می‌پرسد و نمره می‌دهد.
//
// اجرا (برنامه باید روی همان دیتابیسی باشد که golden.json برایش نوشته شده):
//   SAFIR_TOKEN=<JWT کاربرِ دارای دسترسی دستیار> node Tests/ai-eval/run.mjs [--runs 3] [--only debt-1,pl-1]
//   SAFIR_URL پیش‌فرض http://localhost:5170
//
// توکن را از مرورگرِ واردشده بردارید (localStorage). رمز عبور اینجا لازم نیست و نباید باشد.
//
// معیار اصلی «جوابِ غلطِ با اطمینان» است و باید صفر باشد: جوابی که عدد می‌دهد ولی
// عددِ درست را ندارد. «نمی‌دانم» به‌جا، شکست حساب می‌شود ولی خطرناک نیست.
//
// توکن مصرف می‌کند (هر سؤال یک گفتگوی کامل با مدل)؛ برای همین در CI نیست.

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { grade } from './grade.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const base = process.env.SAFIR_URL ?? 'http://localhost:5170';
const token = process.env.SAFIR_TOKEN;
if (!token) { console.error('SAFIR_TOKEN تنظیم نشده است.'); process.exit(2); }

const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const runs = Number(arg('runs', '1'));
const only = arg('only', '')?.split(',').filter(Boolean);

const golden = JSON.parse(readFileSync(join(here, 'golden.json'), 'utf8'));
// سؤال‌هایی که نام واقعی مشتری دارند جدا و بیرون از git نگه داشته می‌شوند
const localFile = join(here, 'golden.local.json');
if (existsSync(localFile)) golden.questions.push(...JSON.parse(readFileSync(localFile, 'utf8')).questions);
const questions = golden.questions.filter(q => !only?.length || only.includes(q.id));

async function ask(question) {
  const t0 = Date.now();
  const res = await fetch(`${base}/api/ai/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ ConversationId: crypto.randomUUID(), Question: question, History: [] })
  });
  const j = await res.json().catch(() => ({}));
  return {
    status: res.status,
    ms: Date.now() - t0,
    text: j.text ?? j.Text ?? '',
    error: j.error ?? j.Error ?? null,
    tools: (j.steps ?? j.Steps ?? []).map(s => s.tool ?? s.Tool)
  };
}

const results = [];
for (const q of questions) {
  for (let r = 1; r <= runs; r++) {
    const a = await ask(q.q);
    // خطای زیرساخت (سقف پیام روزانه، سرور خاموش) نمره‌ی مدل نیست؛ ادامه دادن
    // فقط بقیه‌ی سؤال‌ها را «رد» نشان می‌داد.
    if (a.status !== 200 || /سقف .*پیام|quota/i.test(a.error ?? '')) {
      console.error(`متوقف شد (${q.id}): ${a.error ?? 'HTTP ' + a.status}`);
      process.exit(3);
    }
    const g = a.error ? { ok: false, confidentWrong: false, toolOk: false, notes: [`خطا: ${a.error}`] } : grade(q, a.text, a.tools);
    results.push({ id: q.id, run: r, sec: Math.round(a.ms / 1000), ...g, tools: a.tools, text: a.text });
    const mark = g.ok ? 'PASS' : g.confidentWrong ? 'WRONG' : 'FAIL';
    console.log(`${mark.padEnd(5)} ${q.id.padEnd(10)} run${r} ${String(Math.round(a.ms / 1000)).padStart(3)}s  ${g.notes.join('; ')}`);
  }
}

const n = results.length;
const pass = results.filter(r => r.ok).length;
const wrong = results.filter(r => r.confidentWrong).length;
const tool = results.filter(r => r.toolOk).length;
const avg = Math.round(results.reduce((s, r) => s + r.sec, 0) / Math.max(n, 1));

console.log(`\nدرست: ${pass}/${n}   غلطِ با اطمینان: ${wrong}   ابزار درست: ${tool}/${n}   میانگین زمان: ${avg}s`);

mkdirSync(join(here, 'results'), { recursive: true });
const file = join(here, 'results', `${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
writeFileSync(file, JSON.stringify({ base, runs, snapshot: golden.snapshot, summary: { n, pass, wrong, tool, avg }, results }, null, 2));
console.log(`نتیجه‌ی کامل: ${file}`);

process.exit(wrong > 0 ? 1 : 0);
