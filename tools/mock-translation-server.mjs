// Local-only development fixture. No real API keys or outbound requests.
import http from 'node:http';
const port = Number(process.argv[2] || 18765);
const mappings = new Map([
  ['你好，世界。', 'Hello, world.'], ['你好世界', 'Hello World'],
  ['第一行。', 'First line.'], ['第二行。', 'Second line.'],
  ['测试快速替换', 'Quick translation works'], ['测试浮窗翻译', 'Popup translation works'],
  ['明天下午三点见。', 'See you tomorrow at 3 PM.'],
]);
http.createServer(async (request, response) => {
  response.setHeader('content-type', 'application/json; charset=utf-8');
  if (request.url === '/v1/models') return response.end(JSON.stringify({ data: [{ id: 'mock' }, { id: 'mock-alt' }] }));
  if (request.url !== '/v1/chat/completions' || request.method !== 'POST') { response.statusCode = 404; return response.end('{}'); }
  const chunks = []; let size = 0;
  for await (const chunk of request) { size += chunk.length; if (size > 32768) { response.statusCode = 413; response.end('{}'); return; } chunks.push(chunk); }
  let body;
  try { body = JSON.parse(Buffer.concat(chunks)); } catch { response.statusCode = 400; return response.end('{}'); }
  const text = body.messages?.find((m) => m.role === 'user')?.content || '';
  if (text.includes('失败')) { response.statusCode = 429; response.setHeader('retry-after', '1'); return response.end(JSON.stringify({ error: { message: 'fixture only' } })); }
  if (text.includes('慢')) await new Promise((r) => setTimeout(r, 2000));
  const lines = text.replaceAll('\r\n', '\n').replaceAll('\r', '\n').split('\n');
  const translate = (line) => !line.trim() ? '' : mappings.get(line) || 'Translated fixture text';
  const content = lines.length > 1 ? JSON.stringify({ lines: lines.map(translate) }) : translate(text);
  response.end(JSON.stringify({ choices: [{ finish_reason: 'stop', message: { content } }] }));
}).listen(port, '127.0.0.1', () => process.stdout.write(`Local translation fixture: http://127.0.0.1:${port}/v1\n`));
