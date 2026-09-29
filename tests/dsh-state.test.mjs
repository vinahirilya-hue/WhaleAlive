import test from 'node:test';
import assert from 'node:assert/strict';
import { createActivityTracker } from '../bridge/dsh-state.mjs';
test('DSH multiple sessions preserve working/approval priority and completion expiry',()=>{
 let now=0;const s=createActivityTracker(()=>now);
 s.event('a',{type:'turn/start'});assert.equal(s.state(),'think');
 s.event('a',{type:'tool/call'});assert.equal(s.state(),'working');
 s.event('b',{type:'turn/start'});s.event('b',{type:'turn/end',data:{reason:{kind:'blocked'}}});assert.equal(s.state(),'wait');
 s.event('b',{type:'turn/start'});assert.equal(s.state(),'working');
 s.event('a',{type:'turn/end',data:{reason:{kind:'completed'}}});assert.equal(s.state(),'think');
 s.event('b',{type:'turn/end',data:{reason:{kind:'completed'}}});assert.equal(s.state(),'celebrate');
 now=2600;assert.equal(s.state(),'idle');
});
test('aborted turns do not celebrate; request errors expire; irrelevant data is ignored',()=>{
 let now=0;const s=createActivityTracker(()=>now);
 s.event('a',{type:'turn/start'});s.event('a',{type:'turn/end',data:{reason:{kind:'aborted'}}});assert.equal(s.state(),'idle');
 s.event(null,{type:'turn/start'});s.event('a',{type:'user/message',data:{text:'not retained'}});assert.equal(s.state(),'idle');
 s.error();assert.equal(s.state(),'error');now=3000;assert.equal(s.state(),'idle');
});
