import { request } from './pet.mjs';
import { createActivityTracker } from './dsh-state.mjs';
export const name='whale-alive-state';
export function apply(ctx){
  ctx.effect(()=>{
    const tracker=createActivityTracker();let running=true,inFlight=false;
    const sync=async()=>{if(!running||inFlight)return;inFlight=true;try{await request({action:'state',state:tracker.state(),source:'dsh'},1000);}catch{}finally{inFlight=false;}};
    const disposeEvent=ctx.on('session/event',(session,event)=>{tracker.event(session?.id,event);void sync();});
    const disposeError=ctx.on('agent/request-error',()=>{tracker.error();void sync();});
    const timer=setInterval(sync,2000);timer.unref?.();void sync();
    return ()=>{running=false;clearInterval(timer);disposeEvent();disposeError();};
  });
}
