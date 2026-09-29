// Only activity state is retained. No prompts, messages, paths or credentials leave DSH.
export function createActivityTracker(now = Date.now) {
  const sessions=new Map(); let burst=null;
  return {
    event(id,event){
      if(typeof id!=='string'||!event)return;
      if(event.type==='turn/start')sessions.set(id,'think');
      else if(event.type==='tool/call'&&sessions.has(id))sessions.set(id,'working');
      else if(event.type==='turn/end'){
        const reason=event.data?.reason?.kind;
        if(reason==='blocked')sessions.set(id,'wait');
        else {sessions.delete(id);if(reason==='completed')burst={state:'celebrate',until:now()+2500};else if(reason==='error')burst={state:'error',until:now()+2500};}
      }
    },
    error(){burst={state:'error',until:now()+2500};},
    state(){
      const values=[...sessions.values()];
      if(values.includes('wait'))return 'wait';
      if(burst?.state==='error'&&burst.until>now())return 'error';
      if(values.includes('working'))return 'working';
      if(values.includes('think'))return 'think';
      if(burst&&burst.until>now())return burst.state;
      return 'idle';
    },
  };
}
