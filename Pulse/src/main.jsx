import { createRoot } from 'react-dom/client';
import { App } from './App.jsx';
import './pulse.css';

// Blazor (Client/Pages/Pulse/PulsePage.razor) این ماژول را با import() بار می‌کند و
// mount(element, data, dotNetRef) را صدا می‌زند؛ همان mount با داده‌ی تازه به‌روزرسانی است.
const roots = new WeakMap();

function ensureCss() {
  if (document.querySelector('link[data-pulse-css]')) return;
  const self = new URL(import.meta.url);
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL('./pulse.css' + self.search, self).href; // همان ?v= ِ خودِ ماژول
  link.dataset.pulseCss = '1';
  document.head.appendChild(link);
}

export function mount(el, data, dotnet) {
  ensureCss();
  let root = roots.get(el);
  if (!root) {
    root = createRoot(el);
    roots.set(el, root);
  }
  root.render(<App data={data} dotnet={dotnet} />);
}

export function unmount(el) {
  const root = roots.get(el);
  if (!root) return;
  root.unmount();
  roots.delete(el);
}
