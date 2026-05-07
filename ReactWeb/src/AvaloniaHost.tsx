import { useEffect, useRef, useState } from 'react';

const scriptId = 'avalonia-wasm-entrypoint';

export function AvaloniaHost() {
  const [status, setStatus] = useState('Nacitani obchodniho dashboardu...');
  const started = useRef(false);

  useEffect(() => {
    if (started.current) {
      return;
    }

    started.current = true;

    const existing = document.getElementById(scriptId);
    if (existing) {
      setStatus('');
      return;
    }

    const script = document.createElement('script');
    script.id = scriptId;
    script.type = 'module';
    script.src = '/avalonia/main.js';
    script.onload = () => setStatus('');
    script.onerror = () => {
      setStatus('Avalonia bundle chybi. Spust npm run build:avalonia a obnov stranku.');
    };

    document.body.appendChild(script);
  }, []);

  return (
    <main className="shell">
      <div id="out" className="avalonia-root" aria-label="Trading Dashboard">
        {status && <div className="status">{status}</div>}
      </div>
    </main>
  );
}
