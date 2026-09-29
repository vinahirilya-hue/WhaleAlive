/* Shared, dependency-free sequence operations. */
globalThis.FrameCore = (() => {
  const collator = new Intl.Collator('zh-CN', { numeric: true, sensitivity: 'base' });
  const sortFiles = files => [...files].sort((a, b) => collator.compare(a.webkitRelativePath || a.name, b.webkitRelativePath || b.name));
  const activeIndices = frames => frames.flatMap((f, i) => f.keep ? [i] : []);
  function step(indices, current, direction = 1) {
    if (!indices.length) return -1;
    const at = indices.indexOf(current);
    if (at >= 0) return indices[(at + direction + indices.length) % indices.length];
    return direction > 0 ? (indices.find(i => i > current) ?? indices[0]) : ([...indices].reverse().find(i => i < current) ?? indices.at(-1));
  }
  function advance(indices, current, count, loop) {
    if (!indices.length) return { index: -1, ended: true };
    const next = Math.max(0, indices.indexOf(current)) + count;
    if (!loop && next >= indices.length) return { index: indices.at(-1), ended: true };
    return { index: indices[next % indices.length], ended: false };
  }
  const crcTable = Uint32Array.from({ length: 256 }, (_, n) => {
    for (let i = 0; i < 8; i++) n = (n >>> 1) ^ ((n & 1) ? 0xedb88320 : 0);
    return n >>> 0;
  });
  function crc32(bytes) {
    let crc = 0xffffffff;
    for (const b of bytes) crc = (crc >>> 8) ^ crcTable[(crc ^ b) & 255];
    return (crc ^ 0xffffffff) >>> 0;
  }
  // Store-only ZIP preserves source PNG bytes; no compression library required.
  async function zip(entries) {
    if (entries.length > 65535) throw new Error('导出文件数量过多。');
    const enc = new TextEncoder(), local = [], central = [];
    let offset = 0, centralSize = 0;
    for (const entry of entries) {
      const name = enc.encode(entry.name), data = new Uint8Array(await entry.blob.arrayBuffer()), crc = crc32(data);
      if (offset + data.length + name.length + 30 > 0xffffffff) throw new Error('导出超过 4 GB，请分批处理。');
      const head = new Uint8Array(30 + name.length), h = new DataView(head.buffer);
      h.setUint32(0, 0x04034b50, true); h.setUint16(4, 20, true); h.setUint16(6, 0x800, true);
      h.setUint16(12, 33, true); h.setUint32(14, crc, true); h.setUint32(18, data.length, true); h.setUint32(22, data.length, true); h.setUint16(26, name.length, true); head.set(name, 30);
      local.push(head, data);
      const record = new Uint8Array(46 + name.length), c = new DataView(record.buffer);
      c.setUint32(0, 0x02014b50, true); c.setUint16(4, 20, true); c.setUint16(6, 20, true); c.setUint16(8, 0x800, true);
      c.setUint16(14, 33, true); c.setUint32(16, crc, true); c.setUint32(20, data.length, true); c.setUint32(24, data.length, true); c.setUint16(28, name.length, true); c.setUint32(42, offset, true); record.set(name, 46);
      central.push(record); centralSize += record.length; offset += head.length + data.length;
    }
    const end = new Uint8Array(22), e = new DataView(end.buffer);
    e.setUint32(0, 0x06054b50, true); e.setUint16(8, entries.length, true); e.setUint16(10, entries.length, true); e.setUint32(12, centralSize, true); e.setUint32(16, offset, true);
    return new Blob([...local, ...central, end], { type: 'application/zip' });
  }
  return { sortFiles, activeIndices, step, advance, crc32, zip };
})();
