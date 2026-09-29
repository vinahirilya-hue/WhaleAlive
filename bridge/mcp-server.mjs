import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import { request } from './pet.mjs';

const server=new McpServer({name:'whale-alive',version:'0.1.0'});
const invoke=async(command,extra)=>{
  const abort=()=>request({action:'stop'}).catch(()=>{});
  extra?.signal?.addEventListener('abort',abort,{once:true});
  try {const r=await request(command);return {content:[{type:'text',text:JSON.stringify(r)}],isError:!r.ok};}
  catch(e){return {content:[{type:'text',text:e.message}],isError:true};}
  finally{extra?.signal?.removeEventListener('abort',abort);}
};
server.registerTool('desktop_list_icons',{description:'列出真实 Windows 桌面图标及位置。只读。',inputSchema:{},annotations:{readOnlyHint:true}},(_,e)=>invoke({action:'list'},e));
server.registerTool('desktop_carry_icon',{description:'抱起或拖动图标副本。real=false 预演；real=true 只改变图标位置且支持撤销，必须用户在桌宠设置中开启权限。drag=true 用拉扯步态。不会移动文件路径。',inputSchema:{name:z.string().min(1).max(1024),destination:z.enum(['右下角','左下角','右上角','桌面中央']),real:z.boolean().default(false),drag:z.boolean().default(false)}},(a,e)=>invoke({action:'carry',...a},e));
server.registerTool('pet_status',{description:'读取桌宠状态及本次运行的权限。',inputSchema:{},annotations:{readOnlyHint:true}},(_,e)=>invoke({action:'status'},e));
server.registerTool('pet_say',{description:'让桌宠显示一句简短的话。',inputSchema:{text:z.string().min(1).max(80)}},(a,e)=>invoke({action:'say',...a},e));
server.registerTool('pet_state',{description:'设置陪伴动画；搬运结束后才应用。可在思考、等审批、完成任务时调用。',inputSchema:{state:z.enum(['idle','think','working','wait','celebrate','sleep','error'])}},(a,e)=>invoke({action:'state',...a},e));
server.registerTool('pet_stop',{description:'立即取消当前桌宠动作，释放鼠标。',inputSchema:{}},(_,e)=>invoke({action:'stop'},e));
server.registerTool('pet_undo',{description:'撤销最近一次真实图标位置移动，恢复原位。',inputSchema:{}},(_,e)=>invoke({action:'undo'},e));
server.registerTool('cursor_grab',{description:'用户明确要求鼠标互动时调用。3 秒倒计时后短暂轻拉鼠标，最多 0.9 秒。快速移动、点击或双击 Esc 立即退出。必须在桌宠设置中开启鼠标权限。',inputSchema:{}},(_,e)=>invoke({action:'cursor'},e));
server.registerTool('pet_companion_status',{description:'读取待办、文件暂存与窗口权限。',inputSchema:{},annotations:{readOnlyHint:true}},(_,e)=>invoke({action:'companion',operation:'status'},e));
server.registerTool('pet_todo',{description:'用户要求记录待办时添加泡泡，或按 id 标为完成。',inputSchema:{operation:z.enum(['todo_add','todo_complete']),text:z.string().max(160).optional(),id:z.string().max(64).optional()}},(a,e)=>invoke({action:'companion',...a},e));
server.registerTool('pet_deliver_artifact',{description:'用户授权交付本机已有产物时，把文件图标送到桌宠旁并加入产物架。只保存文件位置，不执行或上传文件。',inputSchema:{path:z.string().min(3).max(2048)}},(a,e)=>invoke({action:'companion',operation:'deliver',...a},e));
server.registerTool('pet_play',{description:'图标借玩、光标捉迷藏、找指针或把指针带到图标；guide_cursor 需用户启用鼠标权限，不自动点击。',inputSchema:{operation:z.enum(['borrow','hide_cursor','find_cursor','guide_cursor']),name:z.string().max(1024).optional()}},(a,e)=>invoke({action:'companion',...a},e));
server.registerTool('pet_list_windows',{description:'只读列出可选普通窗口，用于窗口挪动。',inputSchema:{},annotations:{readOnlyHint:true}},(_,e)=>invoke({action:'companion',operation:'windows'},e));
server.registerTool('pet_window',{description:'推拉窗口并支持撤销。移动需在桌宠设置中手动开启；窗口最大化/最小化时停止。',inputSchema:{operation:z.enum(['move_window','window_undo']),handle:z.number().int().positive().optional(),direction:z.enum(['left','right']).optional()}},(a,e)=>invoke({action:'companion',...a},e));
server.registerTool('pet_icon_plan',{description:'生成、预览或执行用户选定图标的位置整理计划。执行必须在用户审阅计划后请求，且在桌宠设置中开启真实图标权限。只排列选中图标，不搬移文件路径。',inputSchema:{operation:z.enum(['plan_icons','preview_plan','apply_plan']),names:z.array(z.string().min(1).max(1024)).min(1).max(12).optional()}},(a,e)=>invoke({action:'companion',...a},e));
await server.connect(new StdioServerTransport());
