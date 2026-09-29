import net from 'node:net';

export function request(command, timeout = 600000) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection('\\\\.\\pipe\\WhaleAlive.Prototype');
    let data = '';
    socket.setTimeout(timeout, () => socket.destroy(new Error('桌宠响应超时')));
    socket.once('connect', () => socket.write(JSON.stringify(command) + '\n'));
    socket.on('data', chunk => { data += chunk; if (data.includes('\n')) { socket.end(); try { resolve(JSON.parse(data.split('\n')[0])); } catch (e) { reject(e); } } });
    socket.once('error', e => reject(new Error(e.code === 'ENOENT' ? '请先启动 Whale Alive 桌宠。' : e.message)));
    socket.once('end', () => { if (!data.includes('\n')) reject(new Error('桌宠连接已关闭')); });
  });
}

if (process.argv[1] && import.meta.url === (await import('node:url')).pathToFileURL(process.argv[1]).href) {
  const [action='status', ...args] = process.argv.slice(2);
  let command = { action };
  if (action === 'carry') command = {action, name:args[0], destination:args[1] || '右下角', real:args.includes('--real')};
  if (action === 'say') command.text = args.join(' ');
  if (action === 'state') command.state = args[0];
  if (action === 'animation') { command.name = args[0]; command.right = args.includes('--right'); }
  if (action === 'carry') command.drag = args.includes('--drag');
  if (action === 'companion') command = { action, operation:args[0] || 'status', ...(args[1] ? JSON.parse(args[1]) : {}) };
  try { const result=await request(command); console.log(JSON.stringify(result,null,2)); if(!result.ok)process.exitCode=1; }
  catch(e){ console.error(e.message); process.exitCode=1; }
}
