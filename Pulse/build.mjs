// خروجیِ یک‌تکه (ESM) برای Blazor: صفحه‌ی /pulse آن را با import() بار می‌کند.
// خروجی در git هست تا build ِ .NET و CI به Node نیاز نداشته باشند؛ بعد از هر تغییر: npm run build
import * as esbuild from 'esbuild';

const options = {
  entryPoints: ['src/main.jsx'],
  outfile: '../Client/wwwroot/js/pulse/pulse.js',
  bundle: true,
  format: 'esm',
  minify: true,
  target: ['es2020'],
  jsx: 'automatic',
  legalComments: 'eof',
  define: { 'process.env.NODE_ENV': '"production"' },
  logLevel: 'info'
};

if (process.argv.includes('--watch')) {
  const ctx = await esbuild.context(options);
  await ctx.watch();
} else {
  await esbuild.build(options);
}
