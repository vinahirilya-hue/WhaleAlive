import test from 'node:test';
import assert from 'node:assert/strict';
import '../tools/sequence-viewer/core.js';
const { sortFiles, activeIndices, step, advance, crc32, zip } = globalThis.FrameCore;

test('PNG filenames sort numerically without mutating input', () => {
  const files = ['frame_10.png','frame_2.png','frame_1.png'].map(name => ({name}));
  assert.deepEqual(sortFiles(files).map(f => f.name), ['frame_1.png','frame_2.png','frame_10.png']);
  assert.equal(files[0].name, 'frame_10.png');
});
test('excluded frames never occur in the loop, including across wraparound', () => {
  const active = activeIndices([true,false,true,false,true].map(keep => ({keep})));
  let index = 0; const seen = [];
  for (let i=0;i<9;i++) { index = advance(active,index,1,true).index; seen.push(index); }
  assert.deepEqual(seen,[2,4,0,2,4,0,2,4,0]);
  assert.equal(step(active,1,1),2); assert.equal(step(active,3,-1),2);
});
test('empty sequence and single retained frame are safe', () => {
  assert.equal(step([],0),-1);
  assert.deepEqual(advance([],0,1,true),{index:-1,ended:true});
  assert.deepEqual(advance([7],7,999,true),{index:7,ended:false});
  assert.deepEqual(advance([7],7,1,false),{index:7,ended:true});
});
test('non-looping playback ends on last retained frame even after delayed rendering', () => {
  assert.deepEqual(advance([0,3,8],0,10,false),{index:8,ended:true});
  assert.deepEqual(advance([0,3,8],0,1,false),{index:3,ended:false});
});
test('ZIP stores exact PNG bytes with valid CRC, names and directory offsets', async () => {
  const bytes = Uint8Array.of(137,80,78,71,13,10,26,10,0,255);
  assert.equal(crc32(new TextEncoder().encode('123456789')),0xcbf43926);
  const blob = await zip([{name:'frames/frame_0001.png',blob:new Blob([bytes])},{name:'animation.json',blob:new Blob(['{"fps":12}'])}]);
  const data = new Uint8Array(await blob.arrayBuffer()), v = new DataView(data.buffer);
  assert.equal(v.getUint32(0,true),0x04034b50);
  const nameLength = v.getUint16(26,true);
  assert.equal(new TextDecoder().decode(data.slice(30,30+nameLength)),'frames/frame_0001.png');
  assert.deepEqual(data.slice(30+nameLength,30+nameLength+bytes.length),bytes);
  assert.equal(v.getUint32(14,true),crc32(bytes));
  const end = data.length-22; assert.equal(v.getUint32(end,true),0x06054b50);
  assert.equal(v.getUint16(end+10,true),2);
  const central = v.getUint32(end+16,true);
  assert.equal(v.getUint32(central,true),0x02014b50);
  assert.equal(v.getUint32(central+42,true),0);
});
