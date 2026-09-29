import { writeFile, rename, mkdir } from 'node:fs/promises';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
export const name='whale-alive-health-v04';
export const inject=['tools'];
const expected=['desktop_list_icons','desktop_carry_icon','pet_status','pet_say','pet_state','pet_stop','pet_undo','cursor_grab','pet_companion_status','pet_todo','pet_deliver_artifact','pet_play','pet_list_windows','pet_window','pet_icon_plan'];
const reportPath=fileURLToPath(new URL('../artifacts/dsh-connection.json',import.meta.url));
export function apply(ctx){
  ctx.effect(()=>{
    let disposed=false,inFlight=false;
    const publish=async()=>{
      if(disposed||inFlight)return;inFlight=true;
      const path=reportPath;
      try{
        const registered=expected.filter(name=>ctx.tools.get('mcp__whale_alive__'+name));
        const retiredRegistered=['pet_focus','pet_memory','pet_weather_bottle','pet_icon_scene'].filter(name=>ctx.tools.get('mcp__whale_alive__'+name));
        await mkdir(dirname(path),{recursive:true});
        await writeFile(path+'.tmp',JSON.stringify({updatedAt:new Date().toISOString(),expected:expected.length,registered,retiredRegistered,ready:registered.length===expected.length&&retiredRegistered.length===0},null,2));
        await rename(path+'.tmp',path);
      }catch{/* Diagnostic file contention must not interrupt DSH sessions. */}
      finally{inFlight=false;}
    };
    const timer=setInterval(publish,5000);timer.unref?.();void publish();
    return()=>{disposed=true;clearInterval(timer);};
  });
}
